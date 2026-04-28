using Microsoft.Maui.Controls;
using System;

namespace NovaStreamMobile.Views
{
    public partial class HomeView : ContentPage
    {
        public HomeView()
        {
            InitializeComponent();
        }

        private void OnNavigateToSources(object? sender, EventArgs e)
        {
            try
            {
                if (Shell.Current != null && Shell.Current.Items.Count > 0)
                {
                    var tabBar = Shell.Current.Items[0];
                    if (tabBar.Items.Count > 2)
                        Shell.Current.CurrentItem = tabBar.Items[2];
                }
            }
            catch { }
        }

        private void OnNavigateToLiveTv(object? sender, EventArgs e)
        {
            try
            {
                if (Shell.Current != null && Shell.Current.Items.Count > 0)
                {
                    var tabBar = Shell.Current.Items[0];
                    if (tabBar.Items.Count > 1)
                        Shell.Current.CurrentItem = tabBar.Items[1];
                }
            }
            catch { }
        }
    }
}
