using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AtualizaAPP
{
    public partial class MainWindow : Window
    {
        private readonly Services.UpdaterService _updater;
        private Models.UpdateCheckResult? _lastCheck;
        private readonly CancellationTokenSource _cts = new();

        public MainWindow()
        {
            InitializeComponent();
            var cfg = LoadConfig();
            OwnerBox.Text = cfg.GitHub.Owner;
            RepoBox.Text = cfg.GitHub.Repo;
            AssetFilterBox.Text = cfg.GitHub.AssetNameContains;
            ProcBox.Text = cfg.Target.ProcessName;
            ExeBox.Text = cfg.Target.MainExeName;

            _updater = new Services.UpdaterService(Log, SetStatus, SetProgress, cfg);
        }

        private Config LoadConfig()
        {
            const string cfgName = "AtualizaAPP.config.json";
            try
            {
                var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cfgName);
                if (File.Exists(p))
                {
                    var json = File.ReadAllText(p);
                    var cfg = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (cfg != null) return cfg;
                }
            }
            catch { }
            return Config.Default();
        }

        private void ApplyUiConfigToService()
        {
            _updater.Config.GitHub.Owner = OwnerBox.Text.Trim();
            _updater.Config.GitHub.Repo = RepoBox.Text.Trim();
            _updater.Config.GitHub.AssetNameContains = AssetFilterBox.Text.Trim();
            _updater.Config.Target.ProcessName = ProcBox.Text.Trim();
            _updater.Config.Target.MainExeName = ExeBox.Text.Trim();
        }

        private async void CheckBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyUiConfigToService();
                ToggleButtons(false);
                Log("Verificando atualizações...");
                SetStatus("Verificando atualizações...");
                SetProgress(0, "");
                _lastCheck = await _updater.CheckForUpdatesAsync(_cts.Token);
                if (!_lastCheck.Success)
                {
                    SetStatus($"Falha: {_lastCheck.Message}");
                    return;
                }
                if (_lastCheck.UpdateAvailable)
                {
                    SetStatus($"Atualização disponível: {_lastCheck.LocalVersion} → {_lastCheck.RemoteVersion}");
                }
                else
                {
                    SetStatus($"Já está na última versão ({_lastCheck.LocalVersion}).");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Erro na verificação: " + ex.Message);
            }
            finally
            {
                ToggleButtons(true);
            }
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyUiConfigToService();
                ToggleButtons(false);
                Log("Iniciando atualização...");
                SetStatus("Iniciando atualização...");
                SetProgress(0, "");
                var outcome = await _updater.PerformUpdateAsync(_cts.Token);

                if (outcome.Success)
                {
                    // Abre a tela de sucesso e fecha a principal
                    var win = new SuccessWindow(outcome.OldVersion, outcome.NewVersion, onOpenTarget: () =>
                    {
                        _updater.LaunchTargetApp();
                    });
                    win.Show();
                    Close(); // fecha a Main
                }
                else
                {
                    SetStatus("Atualização não realizada. Verifique o log.");
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("Operação cancelada.");
            }
            catch (Exception ex)
            {
                SetStatus("Erro: " + ex.Message);
            }
            finally
            {
                // Se a janela principal ainda existir (não foi fechada), reabilita botões
                if (IsLoaded)
                    ToggleButtons(true);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void ToggleButtons(bool enabled)
        {
            CheckBtn.IsEnabled = enabled;
            UpdateBtn.IsEnabled = enabled;
            CloseBtn.IsEnabled = enabled;
        }

        // Helpers de UI
        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
                LogBox.ScrollToEnd();
            });
        }

        private void SetStatus(string msg)
        {
            Dispatcher.Invoke(() => StatusText.Text = msg);
            Log(msg);
        }

        private void SetProgress(double percent, string? extra)
        {
            Dispatcher.Invoke(() =>
            {
                if (percent < 0)
                {
                    Progress.IsIndeterminate = true;
                    ProgressInfo.Text = extra ?? string.Empty;
                }
                else
                {
                    Progress.IsIndeterminate = false;
                    Progress.Value = Math.Clamp(percent, 0, 100);
                    ProgressInfo.Text = string.IsNullOrWhiteSpace(extra)
                        ? $"{Progress.Value:0}%"
                        : $"{Progress.Value:0}% - {extra}";
                }
            });
        }
    }
}
