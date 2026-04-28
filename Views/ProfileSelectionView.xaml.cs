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
        private ObservableCollection<UserProfile> _profiles;

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
                DisplayAlert("Error", $"Could not load profiles: {ex.Message}", "OK");
            }
        }

        private async void OnAddProfileClicked(object? sender, EventArgs e)
        {
            string? name = await DisplayPromptAsync(
                "New Profile",
                "Enter profile name:",
                "Create",
                "Cancel",
                "Profile name",
                maxLength: 20,
                keyboard: Keyboard.Text);

            if (string.IsNullOrWhiteSpace(name))
                return;

            // Ask for optional PIN
            string? pin = await DisplayPromptAsync(
                "PIN (Optional)",
                "Set a PIN code to lock this profile (leave empty for no PIN):",
                "OK",
                "Skip",
                "4-digit PIN",
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

            await DisplayAlert("Success", $"Profile '{name.Trim()}' created!", "OK");
        }

        private async void OnProfileSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count == 0)
                return;

            var profile = e.CurrentSelection[0] as UserProfile;
            if (profile == null)
                return;

            // Reset selection so user can tap same profile again
            ProfilesCollection.SelectedItem = null;

            // Check if profile is locked
            if (profile.IsLocked)
            {
                string? enteredPin = await DisplayPromptAsync(
                    "Locked Profile",
                    $"Enter PIN for {profile.Name}:",
                    "Unlock",
                    "Cancel",
                    "PIN",
                    maxLength: 4,
                    keyboard: Keyboard.Numeric);

                if (enteredPin != profile.PinCode)
                {
                    await DisplayAlert("Error", "Incorrect PIN", "OK");
                    return;
                }
            }

            // Set current profile and navigate
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
                await DisplayAlert("No Profiles", "Create a profile first.", "OK");
                return;
            }

            // Build list of profile names for action sheet
            string[] profileNames = new string[_profiles.Count];
            for (int i = 0; i < _profiles.Count; i++)
                profileNames[i] = _profiles[i].Name;

            string? selected = await DisplayActionSheet(
                "Manage Profiles - Select a profile",
                "Cancel",
                null,
                profileNames);

            if (string.IsNullOrEmpty(selected) || selected == "Cancel")
                return;

            // Find the profile
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
                $"Profile: {targetProfile.Name}",
                "Cancel",
                "Delete",
                "Rename",
                targetProfile.IsLocked ? "Remove PIN" : "Set PIN");

            if (action == null || action == "Cancel")
                return;

            if (action == "Delete")
            {
                bool confirm = await DisplayAlert(
                    "Delete Profile",
                    $"Are you sure you want to delete '{targetProfile.Name}'?",
                    "Delete",
                    "Cancel");

                if (confirm)
                {
                    _profiles.Remove(targetProfile);
                    _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));
                }
            }
            else if (action == "Rename")
            {
                string? newName = await DisplayPromptAsync(
                    "Rename Profile",
                    "Enter new name:",
                    "Rename",
                    "Cancel",
                    targetProfile.Name,
                    maxLength: 20,
                    keyboard: Keyboard.Text);

                if (!string.IsNullOrWhiteSpace(newName))
                {
                    targetProfile.Name = newName.Trim();
                    _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));
                    // Refresh the list
                    ProfilesCollection.ItemsSource = null;
                    ProfilesCollection.ItemsSource = _profiles;
                }
            }
            else if (action == "Set PIN")
            {
                string? pin = await DisplayPromptAsync(
                    "Set PIN",
                    "Enter a 4-digit PIN:",
                    "Set",
                    "Cancel",
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
            else if (action == "Remove PIN")
            {
                targetProfile.PinCode = string.Empty;
                _storageService.SaveProfiles(new System.Collections.Generic.List<UserProfile>(_profiles));
                ProfilesCollection.ItemsSource = null;
                ProfilesCollection.ItemsSource = _profiles;
            }
        }
    }
}
