using System;
using System.Windows;

namespace AtualizaAPP
{
    public partial class SuccessWindow : Window
    {
        public SuccessWindow(Version oldVersion, Version newVersion)
        {
            InitializeComponent();
            OldVersionText.Text = $"v{oldVersion}";
            NewVersionText.Text = $"v{newVersion}";
        }
    }
}
