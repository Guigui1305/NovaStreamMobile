using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

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
            NavigateToTab(4);
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
            try
            {
                // Naviguer vers TV Direct - l'utilisateur sélectionnera la chaîne là-bas
                NavigateToTab(1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomeView] OnChannelTapped error: {ex}");
            }
        }

        private void OnMovieTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                // Naviguer vers Films - l'utilisateur sélectionnera le film là-bas
                NavigateToTab(2);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomeView] OnMovieTapped error: {ex}");
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
