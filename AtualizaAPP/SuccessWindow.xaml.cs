using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;

namespace AtualizaAPP
{
    public partial class SuccessWindow : Window
    {
        private readonly Action _onOpenTarget;
        private bool _opened;

        public SuccessWindow(Version oldVersion, Version newVersion, Action? onOpenTarget = null)
        {
            InitializeComponent();
            OldVersionText.Text = $"v{oldVersion}";
            NewVersionText.Text = $"v{newVersion}";
            _onOpenTarget = onOpenTarget ?? (() => { });
            Loaded += SuccessWindow_Loaded;
        }

        private async void SuccessWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            if (!_opened)
            {
                OpenTarget();
                Close();
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

