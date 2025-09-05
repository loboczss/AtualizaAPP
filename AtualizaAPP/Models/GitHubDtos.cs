using System;
using System.Text.Json.Serialization;

namespace AtualizaAPP.Models
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Version LocalVersion { get; set; } = new Version(0, 0, 0, 0);
        public Version RemoteVersion { get; set; } = new Version(0, 0, 0, 0);
        public bool UpdateAvailable => Success && RemoteVersion > LocalVersion;
        public string? DownloadUrl { get; set; }
        public string? AssetName { get; set; }
        public long ExpectedSize { get; set; }
    }

    // ⬇️ NOVO: usado para retornar sucesso + versões para a tela
    public class UpdateOutcome
    {
        public bool Success { get; set; }
        public Version OldVersion { get; set; } = new Version(0, 0, 0, 0);
        public Version NewVersion { get; set; } = new Version(0, 0, 0, 0);
    }
}
