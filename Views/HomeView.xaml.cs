using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
using System;
using System.Threading.Tasks;

namespace NovaStreamMobile.Views
{
    public partial class HomeView : ContentPage
    {
        // Index des onglets dans AppShell:
        // 0 = Accueil, 1 = TV Direct, 2 = Films, 3 = Series, 4 = Sources, 5 = Reglages

        public HomeView()
        {
            InitializeComponent();
        }

        private void OnNavigateToSources(object? sender, EventArgs e)
        {
            NavigateToTab(4); // Sources
        }

        private void OnNavigateToLiveTv(object? sender, EventArgs e)
        {
            NavigateToTab(1); // TV Direct
        }

        private void OnNavigateToVod(object? sender, EventArgs e)
        {
            NavigateToTab(2); // Films
        }

        private async void OnChannelTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var frame = sender as Frame;
                if (frame?.BindingContext is Channel channel)
                {
                    // Naviguer vers TV Direct et lancer la lecture
                    NavigateToTab(1);
                    await Task.Delay(500);

                    try
                    {
                        var liveTv = Shell.Current?.CurrentPage as LiveTvView;
                        if (liveTv != null)
                        {
                            await liveTv.PlayChannelSafeAsync(channel);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HomeView] PlayChannel error: {ex}");
                    }
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
                        // Naviguer vers l'onglet Films au lieu de TV Direct
                        NavigateToTab(2);
                        await Task.Delay(500);

                        try
                        {
                            var filmsView = Shell.Current?.CurrentPage as FilmsView;
                            // Le film sera lu directement dans l'onglet Films
                            // Pour l'instant on navigue juste vers Films
                        }
                        catch { }
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomeView] NavigateToTab error: {ex}");
            }
        }
    }
}
