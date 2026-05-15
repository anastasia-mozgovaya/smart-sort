using Avalonia.Controls;
using eco.ViewModels;
using System;

namespace eco.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (DataContext is MainWindowViewModel vm)
            {
                vm.StopCamera();
            }
        }
    }
}