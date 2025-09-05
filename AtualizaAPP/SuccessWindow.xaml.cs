using System;
using System.Windows;

namespace AtualizaAPP
{
    public partial class SuccessWindow : Window
    {
        private readonly Action _onOpenTarget;

        public SuccessWindow(Version oldVersion, Version newVersion, Action? onOpenTarget = null)
        {
            InitializeComponent();
            OldVersionText.Text = $"v{oldVersion}";
            NewVersionText.Text = $"v{newVersion}";
            _onOpenTarget = onOpenTarget ?? (() => { });
        }

        private void OpenTargetBtn_Click(object sender, RoutedEventArgs e)
        {
            try { _onOpenTarget(); }
            catch { /* ignore */ }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
