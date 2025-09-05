using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtualizaAPP.Models;

namespace AtualizaAPP.Services
{
    public class GitHubClient
    {
        private readonly HttpClient _http;

        public GitHubClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AtualizaAPP", "1.0"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, CancellationToken ct)
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(s, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, ct);
            return release;
        }

        public async Task DownloadToFileAsync(Uri url, string destPath, IProgress<(long read, long? total)> progress, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = System.IO.File.Create(destPath);
            var buffer = new byte[1024 * 128];
            int read;
            long sum = 0;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                sum += read;
                progress.Report((sum, total));
            }
        }
    }
}
