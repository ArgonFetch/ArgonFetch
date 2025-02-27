using AngleSharp.Html.Parser;
using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Models;

namespace ArgonFetch.Application.Services.DDLFetcherServices
{
    public class TikTokDllFetcherService : IDllFetcher
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly HtmlParser _htmlParser = new HtmlParser();

        public async Task<MediaInformationDto> FetchLinkAsync(string dllName, DllFetcherOptions dllFetcherOptions)
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
                    RequestedUrl = dllName,
                    StreamingUrl = downloadLink,
                    CoverUrl = imageUrl,
                    Title = title,
                    Author = author
                };
            }
            else
            {
                throw new Exception($"Error: {response.StatusCode}");
            }
        }
    }
}
