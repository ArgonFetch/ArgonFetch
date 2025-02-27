using AngleSharp.Html.Parser;
using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace ArgonFetch.Application.Services.DDLFetcherServices
{
    public class TikTokDllFetcherService : IDllFetcher
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly HtmlParser _htmlParser = new HtmlParser();

        public async Task<MediaInformationDto> FetchLinkAsync(string dllName, DllFetcherOptions dllFetcherOptions = null, CancellationToken cancellationToken = default)
        {
            string baseUrl = "https://tmate.cc";
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            // Get session token
            var response = await _httpClient.GetAsync(baseUrl);
            var content = await response.Content.ReadAsStringAsync();
            var document = await _htmlParser.ParseDocumentAsync(content);

            var sessionToken = response.Headers.GetValues("Set-Cookie").FirstOrDefault()?.Split(';').FirstOrDefault()?.Split('=').Last();
            var token = document.QuerySelector("input[name='token']")?.GetAttribute("value") ?? string.Empty;

            // Make POST request
            var actionUrl = $"{baseUrl}/action";
            _httpClient.DefaultRequestHeaders.Add("Cookie", $"session_data={sessionToken}");
            var payload = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("url", dllName),
                new KeyValuePair<string, string>("token", token)
            });

            response = await _httpClient.PostAsync(actionUrl, payload);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsStringAsync();
                document = await _htmlParser.ParseDocumentAsync(data);
                var title = document.QuerySelector("h1")?.TextContent.Trim();
                var author = document.QuerySelector("p")?.TextContent.Trim();
                var imageUrl = document.QuerySelector("img")?.GetAttribute("src") ?? string.Empty;
                var downloadLink = document.QuerySelectorAll("a[href]").FirstOrDefault()?.GetAttribute("href") ?? string.Empty;

                return new MediaInformationDto
                {
                    RequestedUrl = CleanHtml(dllName),
                    StreamingUrl = CleanHtml(downloadLink),
                    CoverUrl = CleanHtml(imageUrl),
                    Title = CleanHtml(title),
                    Author = CleanHtml(author)
                };
            }
            else
            {
                throw new Exception($"Error: {response.StatusCode}");
            }
        }

        private static string CleanHtml(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            input = WebUtility.HtmlDecode(input); // Decode HTML entities
            input = Regex.Replace(input, "<.*?>", string.Empty); // Remove HTML tags
            input = input.Replace("\\\"", "\"").Replace("\"", "").Replace("\\/", "/"); // Fix slashes
            input = input.Replace("\\r", "").Replace("\\n", ""); // Removes newline
            input = Regex.Replace(input, @"\\[\""/]", string.Empty); // Remove escaped characters like \", \/

            // Replace multiple spaces/newlines with a single space
            input = Regex.Replace(input, @"\s+", " ").Trim();

            // Remove Watermark
            if (input.Contains("Download without Watermark"))
                input = input.Split("Download without Watermark")[0];

            return input;
        }
    }
}
