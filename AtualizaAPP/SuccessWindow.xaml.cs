using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace AtualizaAPP
{
    public partial class SuccessWindow : Window
    {
        private readonly Action _onOpenTarget;
        private bool _opened;
        private DispatcherTimer? _countdownTimer;
        private int _countdown = 30;

        public SuccessWindow(Version oldVersion, Version newVersion, Action? onOpenTarget = null)
        {
            InitializeComponent();
            OldVersionText.Text = $"v{oldVersion}";
            NewVersionText.Text = $"v{newVersion}";
            _onOpenTarget = onOpenTarget ?? (() => { });
            Loaded += SuccessWindow_Loaded;
        }

        private void SuccessWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CountdownText.Text = $"O programa será aberto automaticamente em {_countdown} segundos.";
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            _countdown--;
            if (_countdown <= 0)
            {
                _countdownTimer?.Stop();
                if (!_opened)
                {
                    OpenTarget();
                    Close();
                }
            }
            else
            {
                CountdownText.Text = $"O programa será aberto automaticamente em {_countdown} segundos.";
            }
        }

        private void OpenTargetBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenTarget();
            Close();
        }

        private void OpenTarget()
        {
            if (_opened) return;
            _opened = true;
            _countdownTimer?.Stop();
            try { _onOpenTarget(); }
            catch { /* ignore */ }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_opened)
            {
                OpenTarget();
            }
            base.OnClosing(e);
        }
    }
}

