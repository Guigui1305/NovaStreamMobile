using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
using System;
using System.Threading.Tasks;

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

        private async void OnChannelTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var frame = sender as Frame;
                if (frame?.BindingContext is Channel channel)
                {
                    await PlayOnLiveTvAsync(channel.Name, channel.Url, channel.LogoUrl);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", $"Impossible de lancer la lecture: {ex.Message}", "OK");
            }
        }

        private async void OnMovieTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var frame = sender as Frame;
                if (frame?.BindingContext is VodItem movie)
                {
                    bool play = await DisplayAlert(movie.Name, "Lancer la lecture ?", "Lire", "Annuler");
                    if (play && !string.IsNullOrEmpty(movie.Url))
                    {
                        await PlayOnLiveTvAsync(movie.Name, movie.Url, movie.StreamIcon);
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", $"Impossible de lancer la lecture: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Navigation securisee vers l'onglet TV Direct pour lancer la lecture.
        /// Attend que la page soit prete avant d'assigner le channel.
        /// </summary>
        private async Task PlayOnLiveTvAsync(string name, string url, string logoUrl)
        {
            try
            {
                var channel = new Channel
                {
                    Name = name,
                    Url = url,
                    LogoUrl = logoUrl
                };

                // Naviguer vers l'onglet TV Direct
                if (Shell.Current?.Items.Count > 0)
                {
                    var tabBar = Shell.Current.Items[0];
                    if (tabBar.Items.Count > 1)
                    {
                        Shell.Current.CurrentItem = tabBar.Items[1];

                        // Attendre que la navigation soit complete et la page soit prete
                        LiveTvView? liveTv = null;
                        for (int i = 0; i < 20; i++) // Max 2 secondes d'attente
                        {
                            await Task.Delay(100);
                            liveTv = Shell.Current.CurrentPage as LiveTvView;
                            if (liveTv != null) break;
                        }

                        if (liveTv != null)
                        {
                            var vm = liveTv.BindingContext as NovaStreamMobile.ViewModels.LiveTvViewModel;
                            if (vm != null)
                            {
                                vm.SelectedChannel = channel;
                                return;
                            }
                        }

                        // Fallback
                        await DisplayAlert("Info", "Navigation vers le lecteur en cours. Selectionnez l'onglet TV Direct.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", $"Impossible de lancer la lecture: {ex.Message}", "OK");
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
