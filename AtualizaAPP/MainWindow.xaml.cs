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
        private readonly CancellationTokenSource _cts = new();

        public MainWindow()
        {
            InitializeComponent();
            var cfg = LoadConfig();
            _updater = new Services.UpdaterService(Log, SetStatus, SetProgress, cfg);
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            await Task.Delay(3000, _cts.Token);
            SetStatus("Iniciando atualização...");
            SetProgress(0, null);
            var outcome = await _updater.PerformUpdateAsync(_cts.Token);

            if (outcome.Success)
            {
                var win = new SuccessWindow(outcome.OldVersion, outcome.NewVersion, onOpenTarget: () =>
                {
                    _updater.LaunchTargetApp();
                });
                win.Show();
                Close();
            }
            else
            {
                SetStatus("Atualização não realizada. Verifique o log.");
            }
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

        private void Log(string msg)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
        }

        private void SetStatus(string msg)
        {
            Dispatcher.Invoke(() => StatusText.Text = msg);
            Log(msg);
        }

        private void SetProgress(double percent, string? detail)
        {
            Dispatcher.Invoke(() =>
            {
                if (percent < 0)
                {
                    Progress.IsIndeterminate = true;
                }
                else
                {
                    Progress.IsIndeterminate = false;
                    Progress.Value = Math.Clamp(percent, 0, 100);
                }

                if (detail != null)
                    ProgressDetail.Text = detail;
            });
        }
    }
}
