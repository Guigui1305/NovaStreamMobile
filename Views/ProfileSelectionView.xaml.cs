using System;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class ProfileSelectionView : ContentPage
    {
        private readonly StorageService _storageService;
        private ObservableCollection<UserProfile> _profiles = new();

        public ProfileSelectionView()
        {
            InitializeComponent();
            _storageService = new StorageService();
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            try
            {
                var loaded = _storageService.LoadProfiles();
                _profiles = new ObservableCollection<UserProfile>(loaded);
                ProfilesCollection.ItemsSource = _profiles;
            }
            catch (Exception ex)
            {
                _profiles = new ObservableCollection<UserProfile>();
                ProfilesCollection.ItemsSource = _profiles;
                DisplayAlert("Erreur", $"Impossible de charger les profils : {ex.Message}", "OK");
            }
        }

        private async void OnAddProfileClicked(object? sender, EventArgs e)
        {
            string? name = await DisplayPromptAsync(
                "Nouveau profil",
                "Entrez le nom du profil :",
                "Creer",
                "Annuler",
                "Nom du profil",
                maxLength: 20,
                keyboard: Keyboard.Text);

            if (string.IsNullOrWhiteSpace(name))
                return;

            string? pin = await DisplayPromptAsync(
                "Code PIN (optionnel)",
                "Definir un code PIN pour verrouiller ce profil (laisser vide sinon) :",
                "OK",
                "Passer",
                "Code PIN a 4 chiffres",
                maxLength: 4,
                keyboard: Keyboard.Numeric);

            var newProfile = new UserProfile
            {
                Name = name.Trim(),
                AvatarSource = "profile_default.png",
                PinCode = pin ?? string.Empty
            };

            _profiles.Add(newProfile);
            _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));

            await DisplayAlert("Succes", $"Profil '{name.Trim()}' cree !", "OK");
        }

        private async void OnProfileSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count == 0)
                return;

            var profile = e.CurrentSelection[0] as UserProfile;
            if (profile == null)
                return;

            ProfilesCollection.SelectedItem = null;

            if (profile.IsLocked)
            {
                string? enteredPin = await DisplayPromptAsync(
                    "Profil verrouille",
                    $"Entrez le code PIN pour {profile.Name} :",
                    "Deverrouiller",
                    "Annuler",
                    "PIN",
                    maxLength: 4,
                    keyboard: Keyboard.Numeric);

                if (enteredPin != profile.PinCode)
                {
                    await DisplayAlert("Erreur", "Code PIN incorrect", "OK");
                    return;
                }
            }

            _storageService.SetCurrentProfile(profile);
            profile.LastUsed = DateTime.Now;
            _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));

            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new AppShell();
            }
        }

        private async void OnManageProfilesClicked(object? sender, EventArgs e)
        {
            if (_profiles.Count == 0)
            {
                await DisplayAlert("Aucun profil", "Creez d'abord un profil.", "OK");
                return;
            }

            string[] profileNames = new string[_profiles.Count];
            for (int i = 0; i < _profiles.Count; i++)
                profileNames[i] = _profiles[i].Name;

            string? selected = await DisplayActionSheet(
                "Gerer les profils - Selectionnez un profil",
                "Annuler",
                null,
                profileNames);

            if (string.IsNullOrEmpty(selected) || selected == "Annuler")
                return;

            UserProfile? targetProfile = null;
            foreach (var p in _profiles)
            {
                if (p.Name == selected)
                {
                    targetProfile = p;
                    break;
                }
            }

            if (targetProfile == null) return;

            string? action = await DisplayActionSheet(
                $"Profil : {targetProfile.Name}",
                "Annuler",
                "Supprimer",
                "Renommer",
                targetProfile.IsLocked ? "Retirer le PIN" : "Definir un PIN");

            if (action == null || action == "Annuler")
                return;

            if (action == "Supprimer")
            {
                bool confirm = await DisplayAlert(
                    "Supprimer le profil",
                    $"Voulez-vous vraiment supprimer '{targetProfile.Name}' ?",
                    "Supprimer",
                    "Annuler");

                if (confirm)
                {
                    _profiles.Remove(targetProfile);
                    _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));
                }
            }
            else if (action == "Renommer")
            {
                string? newName = await DisplayPromptAsync(
                    "Renommer le profil",
                    "Entrez le nouveau nom :",
                    "Renommer",
                    "Annuler",
                    targetProfile.Name,
                    maxLength: 20,
                    keyboard: Keyboard.Text);

                if (!string.IsNullOrWhiteSpace(newName))
                {
                    targetProfile.Name = newName.Trim();
                    _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));
                    ProfilesCollection.ItemsSource = null;
                    ProfilesCollection.ItemsSource = _profiles;
                }
            }
            else if (action == "Definir un PIN")
            {
                string? pin = await DisplayPromptAsync(
                    "Definir un PIN",
                    "Entrez un code PIN a 4 chiffres :",
                    "Definir",
                    "Annuler",
                    "PIN",
                    maxLength: 4,
                    keyboard: Keyboard.Numeric);

                if (!string.IsNullOrEmpty(pin))
                {
                    targetProfile.PinCode = pin;
                    _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));
                    ProfilesCollection.ItemsSource = null;
                    ProfilesCollection.ItemsSource = _profiles;
                }
            }
            else if (action == "Retirer le PIN")
            {
                targetProfile.PinCode = string.Empty;
                _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));
                ProfilesCollection.ItemsSource = null;
                ProfilesCollection.ItemsSource = _profiles;
            }
        }
    }
}
