using System;
using Microsoft.Maui.Controls;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class SettingsView : ContentPage
    {
        private readonly StorageService _storageService;
        private readonly ImageCacheService _imageCacheService;

        public SettingsView()
        {
            InitializeComponent();
            _storageService = new StorageService();
            _imageCacheService = new ImageCacheService();

            // Charger l'URL EPG sauvegardee
            string savedEpg = Preferences.Get("epg_url", string.Empty);
            if (!string.IsNullOrEmpty(savedEpg))
                EpgUrlEntry.Text = savedEpg;
        }

        private void OnChangeProfileClicked(object? sender, EventArgs e)
        {
            if (Application.Current != null)
            {
                Application.Current.MainPage = new ProfileSelectionView();
            }
        }

        private async void OnUpdateEpgClicked(object? sender, EventArgs e)
        {
            string url = EpgUrlEntry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                await DisplayAlert("Erreur", "Veuillez entrer une URL EPG valide.", "OK");
                return;
            }

            EpgStatusLabel.Text = "Telechargement du guide en cours...";
            try
            {
                var parser = new XmlTvParserService();
                var programs = await parser.ParseFromUrlAsync(url);
                EpgStatusLabel.Text = $"Guide mis a jour : {programs.Count} programmes charges.";
                Preferences.Set("epg_url", url);
            }
            catch (Exception ex)
            {
                EpgStatusLabel.Text = $"Erreur : {ex.Message}";
            }
        }

        private async void OnClearCacheClicked(object? sender, EventArgs e)
        {
            bool confirm = await DisplayAlert(
                "Vider le cache",
                "Voulez-vous supprimer tous les logos en cache ?",
                "Vider",
                "Annuler");

            if (confirm)
            {
                _imageCacheService.ClearCache();
                await DisplayAlert("Succes", "Cache des logos vide.", "OK");
            }
        }

        private async void OnClearXtreamCacheClicked(object? sender, EventArgs e)
        {
            bool confirm = await DisplayAlert(
                "Vider le cache",
                "Voulez-vous supprimer le cache des chaines et categories ?",
                "Vider",
                "Annuler");

            if (confirm)
            {
                XtreamService.ClearCache();
                await DisplayAlert("Succes", "Cache des chaines vide. Les donnees seront rechargees.", "OK");
            }
        }

        private async void OnClearAllDataClicked(object? sender, EventArgs e)
        {
            bool confirm = await DisplayAlert(
                "Supprimer toutes les donnees",
                "Cette action supprimera tous les profils, sources, favoris et l'historique. Continuer ?",
                "Tout supprimer",
                "Annuler");

            if (confirm)
            {
                _storageService.SaveProfiles(new System.Collections.Generic.List<NovaStreamMobile.Models.UserProfile>());
                _storageService.SaveSources(new System.Collections.Generic.List<NovaStreamMobile.Models.MediaSource>());
                _imageCacheService.ClearCache();
                Preferences.Clear();
                await DisplayAlert("Succes", "Toutes les donnees ont ete supprimees.", "OK");

                if (Application.Current != null)
                {
                    Application.Current.MainPage = new ProfileSelectionView();
                }
            }
        }
    }
}
