using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AtualizaAPP.Models;
using AtualizaAPP.Utils;

namespace AtualizaAPP.Services
{
    public class UpdaterService
    {
        public Config Config { get; }
        private readonly GitHubClient _gh = new();
        private readonly Action<string> _log;
        private readonly Action<string> _status;
        private readonly Action<double, string?> _progress;

        // Diretório alvo da atualização (pasta pai do atualizador)
        private string InstallDir => Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        private string ThisExeName => Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        private const string UpdaterDirName = "AtualizaAPP";

        // EXCEÇÕES DA LIMPEZA (nível raiz)
        private static readonly string[] _excludeFileNamesRoot =
        {
            "AtualizaAPP.config.json", // nosso config
            "appsettings.json",        // se existir, preserva também
            "AtualizaAPP.deps.json",
            "syncstats.json",
            "AtualizaAPP.dll",
            "AtualizaAPP.exe",
            "AtualizaAPP.pdb",
            "AtualizaAPP.runtimeconfig.json"
        };

        private static readonly string[] _excludeDirNamesRoot =
        {
            "backup-enviados",
            "backup-erros",
            "backup-pendentes",
            "downloads",
            UpdaterDirName
        };

        public UpdaterService(Action<string> log, Action<string> status, Action<double, string?> progress, Config config)
        {
            _log = log; _status = status; _progress = progress; Config = config;
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct)
        {
            var result = new UpdateCheckResult { Success = false, Message = string.Empty };
            try
            {
                var local = GetLocalTargetVersion(); // ⬅️ agora olha version.txt, exe e dll
                var release = await _gh.GetLatestReleaseAsync(Config.GitHub.Owner, Config.GitHub.Repo, ct);
                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                    return Fail("Não foi possível obter informações do release");

                var remote = ParseVersion(release.TagName!);
                if (remote == null) return Fail($"Tag de versão inválida: {release.TagName}");

                var asset = release.Assets.FirstOrDefault(a =>
                    !string.IsNullOrEmpty(a.Name) &&
                    a.Name.Contains(Config.GitHub.AssetNameContains, StringComparison.OrdinalIgnoreCase));

                if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                    return Fail("Nenhum asset adequado (.zip) encontrado no release");

                result.Success = true;
                result.LocalVersion = local;
                result.RemoteVersion = remote;
                result.DownloadUrl = asset.BrowserDownloadUrl;
                result.AssetName = asset.Name;
                result.ExpectedSize = asset.Size;
                result.Message = remote > local ? "Atualização disponível" : "Já está atualizado";
                return result;
            }
            catch (Exception ex)
            {
                return Fail("Erro na verificação: " + ex.Message);
            }

            UpdateCheckResult Fail(string msg) => new() { Success = false, Message = msg };
        }

        // AGORA RETORNA Outcome (sucesso + versões anterior/nova)
        public async Task<UpdateOutcome> PerformUpdateAsync(CancellationToken ct)
        {
            var outcome = new UpdateOutcome();
            _status("Verificando atualizações...");
            var check = await CheckForUpdatesAsync(ct);
            if (!check.Success)
            {
                _status(check.Message);
                outcome.Success = false;
                outcome.OldVersion = check.LocalVersion;
                outcome.NewVersion = check.RemoteVersion;
                return outcome;
            }
            if (!check.UpdateAvailable)
            {
                _status("Nada para atualizar.");
                outcome.Success = false;
                outcome.OldVersion = check.LocalVersion;
                outcome.NewVersion = check.RemoteVersion;
                return outcome;
            }

            // guarda versões para a tela de sucesso
            outcome.OldVersion = check.LocalVersion;
            outcome.NewVersion = check.RemoteVersion;

            // Dirs temporários
            var tempRoot = Path.Combine(Path.GetTempPath(), "AtualizaAPP_" + Guid.NewGuid().ToString("N"));
            var zipPath = Path.Combine(tempRoot, check.AssetName ?? "update.zip");
            var extractDir = Path.Combine(tempRoot, "extracted");
            var backupDir = Path.Combine(tempRoot, "backup");
            Directory.CreateDirectory(tempRoot);

            try
            {
                // Fechar app alvo
                _status($"Encerrando processo alvo: {Config.Target.ProcessName}...");
                FileUtils.SafeKillProcessesByName(Config.Target.ProcessName);

                // Backup
                _status("Criando backup...");
                _progress(5, "Fazendo backup");
                Directory.CreateDirectory(backupDir);
                var excludeFiles = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { ThisExeName };
                var excludeDirs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { UpdaterDirName };
                FileUtils.CopyDirectory(InstallDir, backupDir, excludeFiles, excludeDirs);

                // Limpar pasta
                _progress(10, "Limpando pasta");
                CleanInstallFolder();

                // Download
                _status("Baixando pacote...");
                long last = 0; long? total = null;
                const double baseOffset = 15.0; // 15 → 75 %
                const double range = 60.0;
                await _gh.DownloadToFileAsync(new Uri(check.DownloadUrl!), zipPath,
                    new Progress<(long read, long? total)>(tuple =>
                    {
                        (last, total) = tuple;
                        if (total.HasValue && total.Value > 0)
                        {
                            var p = baseOffset + (last * range / total.Value);
                            _progress(p, $"{BytesToString(last)} / {BytesToString(total.Value)}");
                        }
                        else
                        {
                            _progress(-1, BytesToString(last)); // indeterminado
                        }
                    }), ct);

                // Validar zip
                using (var za = ZipFile.OpenRead(zipPath))
                {
                    if (za.Entries.Count == 0) throw new InvalidDataException("ZIP vazio");
                }

                // Extrair
                _status("Extraindo pacote...");
                _progress(78, "Extraindo arquivos");
                FileUtils.EnsureEmptyDir(extractDir);
                await ExtractWithProgressAsync(zipPath, extractDir,
                    new Progress<double>(p => _progress(78 + p * 12.0, null)), ct); // 78-90%

                // Aplicar
                _status("Aplicando atualização...");
                _progress(92, "Copiando arquivos");
                ApplyFiles(extractDir);
                _progress(98, "Limpando obsoletos");
                PurgeObsolete(extractDir);

                _progress(100, "Concluído");
                _status("Atualização concluída.");
                outcome.Success = true;
                return outcome;
            }
            catch (Exception ex)
            {
                _status("Falha: " + ex.Message);
                // Restaurar do backup
                try
                {
                    if (Directory.Exists(backupDir))
                    {
                        _status("Restaurando backup...");
                        var excludeFiles = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { ThisExeName };
                        var excludeDirs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { UpdaterDirName };
                        FileUtils.CopyDirectory(backupDir, InstallDir, excludeFiles, excludeDirs);
                    }
                }
                catch { /* ignore */ }
                outcome.Success = false;
                return outcome;
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        public void LaunchTargetApp()
        {
            try
            {
                var exe = Path.Combine(InstallDir, Config.Target.MainExeName);
                if (File.Exists(exe))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        // --------- LIMPEZA DE PASTA (nível raiz) ----------
        private void CleanInstallFolder()
        {
            try
            {
                _status("Limpando pasta de instalação...");
                _log($"Limpando '{InstallDir}' (protegendo arquivos/pastas essenciais).");

                var excludeFiles = new System.Collections.Generic.HashSet<string>(_excludeFileNamesRoot, StringComparer.OrdinalIgnoreCase)
                {
                    ThisExeName // nunca remover o próprio exe do atualizador
                };

                var excludeDirs = new System.Collections.Generic.HashSet<string>(_excludeDirNamesRoot, StringComparer.OrdinalIgnoreCase);

                // Arquivos do nível raiz
                foreach (var file in Directory.EnumerateFiles(InstallDir, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    if (excludeFiles.Contains(name)) continue;
                    try { File.Delete(file); }
                    catch (Exception ex) { _log($"[WARN] Falha ao excluir arquivo '{name}': {ex.Message}"); }
                }

                // Diretórios do nível raiz
                foreach (var dir in Directory.EnumerateDirectories(InstallDir, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
                    if (excludeDirs.Contains(name)) continue;
                    try { Directory.Delete(dir, recursive: true); }
                    catch (Exception ex) { _log($"[WARN] Falha ao excluir diretório '{name}': {ex.Message}"); }
                }

                _log("Limpeza concluída.");
            }
            catch (Exception ex)
            {
                _log("[WARN] Limpeza encontrou erros: " + ex.Message);
            }
        }

        // --------- VERSÃO LOCAL (version.txt, exe, dll) ----------
        private Version GetLocalTargetVersion()
        {
            try
            {
                // 1) version.txt (última linha não vazia)
                var vTxt = TryReadVersionTxt();
                if (vTxt != null) return vTxt;

                // 2) EXE e DLL → pega a maior
                Version? vExe = TryGetAssemblyVersion(Path.Combine(InstallDir, Config.Target.MainExeName));
                var dllPath = Path.Combine(InstallDir, Path.GetFileNameWithoutExtension(Config.Target.MainExeName) + ".dll");
                Version? vDll = TryGetAssemblyVersion(dllPath);

                if (vExe != null && vDll != null) return vExe > vDll ? vExe : vDll;
                if (vExe != null) return vExe;
                if (vDll != null) return vDll;
            }
            catch { }
            return new Version(0, 0, 0, 0);
        }

        private Version? TryReadVersionTxt()
        {
            try
            {
                var p = Path.Combine(InstallDir, "version.txt");
                if (!File.Exists(p)) return null;

                // pega a última linha não vazia
                var lines = File.ReadAllLines(p)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToArray();
                if (lines.Length == 0) return null;

                var last = lines[^1];
                // aceita "v1.2.3.4" ou "1.2.3.4"
                var m = Regex.Match(last, @"^v?(\d+(\.\d+){1,3})$", RegexOptions.IgnoreCase);
                if (!m.Success) return null;

                var raw = m.Groups[1].Value; // só números e pontos
                return Version.TryParse(raw, out var v) ? v : null;
            }
            catch { return null; }
        }

        private static Version? TryGetAssemblyVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var v = AssemblyName.GetAssemblyName(path).Version;
                return v;
            }
            catch { return null; }
        }

        // --------- HELPERS ----------
        private static Version? ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var s = tag.Trim().TrimStart('v', 'V');
            s = new string(s.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
            return Version.TryParse(s, out var v) ? v : null;
        }

        private async Task ExtractWithProgressAsync(string zipPath, string destDir, IProgress<double> progress, CancellationToken ct)
        {
            using var za = ZipFile.OpenRead(zipPath);
            long total = za.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Sum(e => e.Length);
            long done = 0;
            foreach (var entry in za.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name))
                {
                    var dir = Path.Combine(destDir, entry.FullName);
                    Directory.CreateDirectory(dir);
                    continue;
                }
                var targetPath = Path.Combine(destDir, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using var es = entry.Open();
                using var fs = File.Create(targetPath);
                var buffer = new byte[1024 * 128];
                int read;
                while ((read = await es.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress.Report((double)done / total);
                }
            }
            progress.Report(1.0);
        }

        private void ApplyFiles(string fromDir)
        {
            var excludeFiles = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { ThisExeName };
            var excludeDirs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { UpdaterDirName };
            foreach (var dir in Directory.EnumerateDirectories(fromDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(fromDir, dir);
                if (IsInExcludedDir(rel, excludeDirs)) continue;
                Directory.CreateDirectory(Path.Combine(InstallDir, rel));
            }
            foreach (var file in Directory.EnumerateFiles(fromDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(fromDir, file);
                var name = Path.GetFileName(file);
                if (excludeFiles.Contains(name) || IsInExcludedDir(rel, excludeDirs)) continue; // não sobrescrever o atualizador
                var dest = Path.Combine(InstallDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
        }

        private void PurgeObsolete(string fromDir)
        {
            // Preserve the same essential files and folders used during the initial cleaning
            var excludeFiles = new System.Collections.Generic.HashSet<string>(_excludeFileNamesRoot, StringComparer.OrdinalIgnoreCase)
            {
                ThisExeName
            };
            var excludeDirs = new System.Collections.Generic.HashSet<string>(_excludeDirNamesRoot, StringComparer.OrdinalIgnoreCase)
            {
                UpdaterDirName
            };

            var newSet = Directory.EnumerateFiles(fromDir, "*", SearchOption.AllDirectories)
                                  .Select(p => Path.GetRelativePath(fromDir, p))
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(InstallDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(InstallDir, file);
                var name = Path.GetFileName(file);
                if (excludeFiles.Contains(name) || IsInExcludedDir(rel, excludeDirs)) continue;
                if (!newSet.Contains(rel))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(InstallDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                var rel = Path.GetRelativePath(InstallDir, dir);
                if (IsInExcludedDir(rel, excludeDirs)) continue;
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    try { Directory.Delete(dir, false); } catch { }
                }
            }
        }

        private static string BytesToString(long v)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB" };
            double d = v; int i = 0;
            while (d >= 1024 && i < suf.Length - 1) { d /= 1024; i++; }
            return $"{d:0.##} {suf[i]}";
        }

        private static bool IsInExcludedDir(string relativePath, System.Collections.Generic.HashSet<string> excludedDirs)
        {
            foreach (var ex in excludedDirs)
            {
                if (relativePath.Equals(ex, StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith(ex + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
