namespace AtualizaAPP
{
    public class Config
    {
        public GitHubSection GitHub { get; set; } = new();
        public TargetSection Target { get; set; } = new();

        public static Config Default() => new()
        {
            GitHub = new GitHubSection { Owner = "loboczss", Repo = "leituraWPF", AssetNameContains = ".zip" },
            Target = new TargetSection { ProcessName = "LeituraWPF", MainExeName = "LeituraWPF.exe" }
        };
    }

    public class GitHubSection
    {
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
        public string AssetNameContains { get; set; } = ".zip";
    }

    public class TargetSection
    {
        public string ProcessName { get; set; } = string.Empty; // sem .exe
        public string MainExeName { get; set; } = string.Empty; // com .exe
    }
}
