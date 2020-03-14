using System;
using System.Threading.Tasks;
using PuppeteerSharp;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace puppymage
{

    class Program
    {

        static async Task Main(string[] args)
        {

            //http://www.hardkoded.com/blog/running-puppeteer-sharp-azure-functions

            if (args.Length != 2) throw new ArgumentException("Usage: puppymage.exe <path_to_image> <save_to_directory>");

            Directory.CreateDirectory(args[1]);

            await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultRevision);

            var getExtension = new Regex(@"\.(\w{3,4})(?:$|\?)", RegexOptions.Multiline | RegexOptions.Compiled);

            var currentIndex = 0;

            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, Args = new[] { "--no-sandbox" }, DefaultViewport = new ViewPortOptions { Width = 1920, Height = 1024 } }))
            {
                await foreach (var url in GetSimilarImages(browser, args[0], 10))
                {

                    var ext = "png";
                    if (getExtension.IsMatch(url)) ext = getExtension.Match(url).Groups[1].Value;
                    var fileName = new string('0', 8) + ++currentIndex;
                    fileName = fileName.Substring(fileName.Length - 8, 8) + "." + ext;
                    var path = Path.Combine(args[1], fileName);

                    using (var page = await browser.NewPageAsync())
                    {

                        try
                        {
                            page.DefaultTimeout = 10000;

                            var request = page.WaitForRequestAsync(url);
                            var response = page.WaitForResponseAsync(url);
                            await page.GoToAsync(url);

                            if (response.Status == TaskStatus.RanToCompletion)
                            {

                                var code = (int)response.Result.Status;

                                if (code >= 400)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"{url} -> {response.Result.Status}");
                                    Console.ForegroundColor = ConsoleColor.White;
                                }
                                else
                                {
                                    var buffer = await response.Result.BufferAsync();
                                    File.WriteAllBytes(path, buffer);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"{url} -> {fileName}");
                                    Console.ForegroundColor = ConsoleColor.White;
                                }

                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"{url} -> {response.Status}");
                                Console.ForegroundColor = ConsoleColor.White;
                            }

                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"{url} -> {ex.Message}");
                            Console.ForegroundColor = ConsoleColor.White;
                        }

                    }

                }

            }

        }

        static async IAsyncEnumerable<string> GetSimilarImages(Browser browser, string pathToImage, int intervalSeconds)
        {

            //https://gist.github.com/hlaueriksson/4a4199f0802681b06f0f508a2916164d

            var result = new List<string>();

            using (var googlePage = await browser.NewPageAsync())
            {

                await googlePage.SetExtraHttpHeadersAsync(new Dictionary<string, string> { { "Accept-Language", "pt-PT" } });
                await googlePage.GoToAsync($"https://images.google.com");
                await googlePage.WaitForSelectorAsync("form div[aria-label");

                var searchButtons = await googlePage.QuerySelectorAllAsync("form div[aria-label] span");
                var searchByImage = default(ElementHandle);

                foreach (var item in searchButtons)
                {
                    var buttonImage = await item.EvaluateFunctionAsync<string>("element => window.getComputedStyle(element, false).backgroundImage");
                    if (buttonImage.Contains("camera"))
                    {
                        searchByImage = item;
                        break;
                    }
                }

                if (searchByImage == null)
                {
                    yield break;
                }

                await searchByImage.EvaluateFunctionAsync<string>("element => element.id = 'Button1'");
                await googlePage.ClickAsync("#Button1");
                await googlePage.WaitForSelectorAsync("form a[onclick*='(true)']");

                var uploadTab = await googlePage.QuerySelectorAsync("form a[onclick*='(true)']");
                await uploadTab.EvaluateFunctionAsync<string>("element => element.id = 'Button2'");
                await googlePage.ClickAsync("#Button2");

                var input = await googlePage.QuerySelectorAsync("#qbfile");
                await input.UploadFileAsync(pathToImage);
                await googlePage.WaitForSelectorAsync("g-section-with-header h3 a");
                await googlePage.ClickAsync("g-section-with-header h3 a[href*='/search']");

                try
                {
                    await googlePage.WaitForSelectorAsync("#islrg img");
                }
                catch (Exception)
                {
                    yield break;
                }

                var imgCount = (await googlePage.QuerySelectorAllAsync("#islrg img")).Length;
                await googlePage.ClickAsync($"#islrg > div > div:nth-child(1) img");

                var interval = (intervalSeconds * 1000) / 2;

                for (int i = 1; i <= imgCount; i++)
                {

                    await Task.Delay(interval);
                    var images = await googlePage.QuerySelectorAllAsync("#islsp img[src^='http']"); // actual image

                    foreach (var image in images)
                    {
                        var currentUrl = await image.EvaluateFunctionAsync<string>("x => x.src");
                        if (!currentUrl.Contains("gstatic.com") && !result.Contains(currentUrl))
                        {
                            result.Add(currentUrl);
                            yield return currentUrl;
                        }
                    }

                    await googlePage.Keyboard.PressAsync("ArrowRight");
                    await Task.Delay(interval);

                }

            }

        }

    }

}