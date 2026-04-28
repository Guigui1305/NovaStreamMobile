using System;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class SourcesView : ContentPage
    {
        private readonly StorageService _storageService;
        private ObservableCollection<MediaSource> _sources = new();

        public SourcesView()
        {
            InitializeComponent();
            _storageService = new StorageService();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadSources();
        }

        private void LoadSources()
        {
            try
            {
                var loaded = _storageService.LoadSources();
                _sources = new ObservableCollection<MediaSource>(loaded);
                SourcesCollection.ItemsSource = _sources;
            }
            catch { }
        }

        private async void OnAddSourceClicked(object? sender, EventArgs e)
        {
            string? typeChoice = await DisplayActionSheet(
                "Type de source",
                "Annuler",
                null,
                "M3U / M3U8 (lien playlist)",
                "Xtream Codes (serveur + identifiants)");

            if (typeChoice == null || typeChoice == "Annuler")
                return;

            bool isXtream = typeChoice.Contains("Xtream");

            string? name = await DisplayPromptAsync(
                "Nom de la source",
                "Donnez un nom a cette source :",
                "Suivant",
                "Annuler",
                "Ex: Mon IPTV Premium",
                maxLength: 30);

            if (string.IsNullOrWhiteSpace(name))
                return;

            if (isXtream)
            {
                string? url = await DisplayPromptAsync(
                    "URL du serveur",
                    "Entrez l'URL du serveur Xtream :",
                    "Suivant",
                    "Annuler",
                    "http://serveur.com:80");

                if (string.IsNullOrWhiteSpace(url))
                    return;

                string? username = await DisplayPromptAsync(
                    "Nom d'utilisateur",
                    "Entrez votre nom d'utilisateur :",
                    "Suivant",
                    "Annuler",
                    "Nom d'utilisateur");

                if (string.IsNullOrWhiteSpace(username))
                    return;

                string? password = await DisplayPromptAsync(
                    "Mot de passe",
                    "Entrez votre mot de passe :",
                    "Ajouter",
                    "Annuler",
                    "Mot de passe");

                if (string.IsNullOrWhiteSpace(password))
                    return;

                var source = new MediaSource
                {
                    Name = name.Trim(),
                    Url = url.Trim().TrimEnd('/'),
                    Type = SourceType.Xtream,
                    Username = username.Trim(),
                    Password = password.Trim()
                };

                _sources.Add(source);
                _storageService.SaveSources(new System.Collections.Generic.List<MediaSource>(_sources));
                await DisplayAlert("Succes", $"Source '{name.Trim()}' ajoutee !", "OK");
            }
            else
            {
                string? url = await DisplayPromptAsync(
                    "URL M3U",
                    "Entrez l'URL de la playlist M3U :",
                    "Ajouter",
                    "Annuler",
                    "http://exemple.com/playlist.m3u");

                if (string.IsNullOrWhiteSpace(url))
                    return;

                var source = new MediaSource
                {
                    Name = name.Trim(),
                    Url = url.Trim(),
                    Type = SourceType.M3U
                };

                _sources.Add(source);
                _storageService.SaveSources(new System.Collections.Generic.List<MediaSource>(_sources));
                await DisplayAlert("Succes", $"Source '{name.Trim()}' ajoutee !", "OK");
            }
        }

        private async void OnDeleteSourceClicked(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is MediaSource source)
            {
                bool confirm = await DisplayAlert(
                    "Supprimer la source",
                    $"Voulez-vous vraiment supprimer '{source.Name}' ?",
                    "Supprimer",
                    "Annuler");

                if (confirm)
                {
                    _sources.Remove(source);
                    _storageService.SaveSources(new System.Collections.Generic.List<MediaSource>(_sources));
                }
            }
        }
    }
}
