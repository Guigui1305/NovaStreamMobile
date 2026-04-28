using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
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
            NavigateToTab(3);
        }

        private void OnNavigateToLiveTv(object? sender, EventArgs e)
        {
            NavigateToTab(1);
        }

        private void OnNavigateToVod(object? sender, EventArgs e)
        {
            NavigateToTab(2);
        }

        private void OnChannelTapped(object? sender, TappedEventArgs e)
        {
            var frame = sender as Frame;
            if (frame?.BindingContext is Channel channel)
            {
                NavigateToTab(1);
                if (Shell.Current?.CurrentPage is LiveTvView liveTv)
                {
                    var vm = liveTv.BindingContext as NovaStreamMobile.ViewModels.LiveTvViewModel;
                    if (vm != null)
                        vm.SelectedChannel = channel;
                }
            }
        }

        private async void OnMovieTapped(object? sender, TappedEventArgs e)
        {
            var frame = sender as Frame;
            if (frame?.BindingContext is VodItem movie)
            {
                bool play = await DisplayAlert(movie.Name, "Lancer la lecture ?", "Lire", "Annuler");
                if (play && !string.IsNullOrEmpty(movie.Url))
                {
                    var channel = new Channel
                    {
                        Name = movie.Name,
                        Url = movie.Url,
                        LogoUrl = movie.StreamIcon
                    };

                    NavigateToTab(1);
                    if (Shell.Current?.CurrentPage is LiveTvView liveTv)
                    {
                        var vm = liveTv.BindingContext as NovaStreamMobile.ViewModels.LiveTvViewModel;
                        if (vm != null)
                            vm.SelectedChannel = channel;
                    }
                }
            }
        }

        private void NavigateToTab(int tabIndex)
        {
            try
            {
                if (Shell.Current?.Items.Count > 0)
                {
                    var tabBar = Shell.Current.Items[0];
                    if (tabBar.Items.Count > tabIndex)
                        Shell.Current.CurrentItem = tabBar.Items[tabIndex];
                }
            }
            catch { }
        }
    }
}
