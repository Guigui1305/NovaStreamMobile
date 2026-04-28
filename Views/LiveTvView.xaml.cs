using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
using NovaStreamMobile.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
#if ANDROID
using Android.App;
using Android.Util;
using LibVLCSharp.Platforms.Android;
#endif

namespace NovaStreamMobile.Views
{
    public partial class LiveTvView : ContentPage
    {
        private bool _isFullscreen = false;
        private double _currentBrightness = 0.5;
        private double _startBrightness;
        private LiveTvViewModel? _vm;
        private bool _categoriesBuilt = false;

        public LiveTvView()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _vm = BindingContext as LiveTvViewModel;
            AttachVideoView();

            if (_vm != null && !_categoriesBuilt)
            {
                _vm.LiveCategories.CollectionChanged += (s, e) =>
                {
                    MainThread.BeginInvokeOnMainThread(BuildCategoryButtons);
                };
                if (_vm.LiveCategories.Count > 0)
                    BuildCategoryButtons();
            }
        }

        private void BuildCategoryButtons()
        {
            if (_vm == null) return;
            _categoriesBuilt = true;

            CategoriesBar.Children.Clear();

            // Bouton Favoris
            var favBtn = CreateCategoryButton("Favoris", "favorites",
                _vm.SelectedCategoryId == "favorites");
            CategoriesBar.Children.Add(favBtn);

            // Boutons de categories
            foreach (var cat in _vm.LiveCategories)
            {
                string displayName = CleanCategoryName(cat.CategoryName);
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                bool isSelected = cat.CategoryId == _vm.SelectedCategoryId;
                var btn = CreateCategoryButton(displayName, cat.CategoryId, isSelected);
                CategoriesBar.Children.Add(btn);
            }
        }

        private Button CreateCategoryButton(string text, string categoryId, bool isSelected)
        {
            var btn = new Button
            {
                Text = text,
                BackgroundColor = isSelected ? Color.FromArgb("#E50914") : Color.FromArgb("#1F1F1F"),
                TextColor = Colors.White,
                FontSize = 11,
                CornerRadius = 16,
                HeightRequest = 32,
                Padding = new Thickness(12, 0),
                BorderColor = isSelected ? Color.FromArgb("#E50914") : Color.FromArgb("#333333"),
                BorderWidth = 1
            };
            btn.Clicked += (s, e) =>
            {
                if (_vm != null)
                {
                    _vm.SelectedCategoryId = categoryId;
                    BuildCategoryButtons(); // Refresh selection
                }
            };
            return btn;
        }

        private string CleanCategoryName(string name)
        {
            name = name.Replace("|FR|", "").Replace("|CH|", "").Replace("|MULTI|", "").Trim();
            if (name.StartsWith("*")) name = name.TrimStart('*').Trim();
            // Enlever le symbole special au debut
            while (name.Length > 0 && !char.IsLetterOrDigit(name[0]) && name[0] != '(')
                name = name.Substring(1).Trim();
            return name;
        }

        private void AttachVideoView()
        {
            if (_vm?.MediaPlayer == null) return;

#if ANDROID
            try
            {
                var videoView = new LibVLCSharp.Platforms.Android.VideoView(
                    Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
                videoView.MediaPlayer = _vm.MediaPlayer;

                var nativeView = new Microsoft.Maui.Controls.ContentView();
                nativeView.Content = new Label { Text = "" };

                VideoContainer.Content = nativeView;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        var handler = VideoContainer.Handler;
                        if (handler?.PlatformView is Android.Views.ViewGroup container)
                        {
                            container.RemoveAllViews();
                            if (videoView.Parent is Android.Views.ViewGroup oldParent)
                                oldParent.RemoveView(videoView);
                            container.AddView(videoView, new Android.Views.ViewGroup.LayoutParams(
                                Android.Views.ViewGroup.LayoutParams.MatchParent,
                                Android.Views.ViewGroup.LayoutParams.MatchParent));
                        }
                    }
                    catch { }
                });
            }
            catch { }
#endif
        }

        private void OnChannelSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count == 0) return;
            var channel = e.CurrentSelection[0] as Channel;
            if (channel == null) return;

            if (_vm != null)
            {
                _vm.SelectedChannel = channel;
                UpdatePlayPauseButton();
            }

            if (sender is CollectionView cv)
                cv.SelectedItem = null;
        }

        private void UpdatePlayPauseButton()
        {
            if (_vm != null)
                PlayPauseBtn.Text = _vm.IsPlaying ? "||" : ">";
        }

        private async void OnSubtitleClicked(object? sender, EventArgs e)
        {
            if (_vm == null || _vm.Subtitles.Count == 0)
            {
                await DisplayAlert("Sous-titres", "Aucun sous-titre disponible pour cette chaine.", "OK");
                return;
            }

            string[] names = new string[_vm.Subtitles.Count];
            for (int i = 0; i < _vm.Subtitles.Count; i++)
                names[i] = _vm.Subtitles[i].Name;

            string? result = await DisplayActionSheet("Choisir les sous-titres", "Annuler", "Desactiver", names);
            if (result == null || result == "Annuler") return;

            if (result == "Desactiver")
            {
                _vm.MediaPlayer?.SetSpu(-1);
                return;
            }

            foreach (var sub in _vm.Subtitles)
            {
                if (sub.Name == result)
                {
                    _vm.SelectedSubtitle = sub;
                    break;
                }
            }
        }

        private void OnFullscreenClicked(object? sender, EventArgs e)
        {
            _isFullscreen = !_isFullscreen;
            
            if (_isFullscreen)
            {
                Shell.SetTabBarIsVisible(this, false);
                Shell.SetNavBarIsVisible(this, false);
                PlayerRow.Height = new GridLength(1, GridUnitType.Star);
                SearchRow.Height = new GridLength(0);
            }
            else
            {
                Shell.SetTabBarIsVisible(this, true);
                Shell.SetNavBarIsVisible(this, true);
                PlayerRow.Height = new GridLength(220);
                SearchRow.Height = GridLength.Auto;
            }
        }

        private void OnPipClicked(object? sender, EventArgs e)
        {
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity != null)
                {
                    var builder = new PictureInPictureParams.Builder();
                    builder.SetAspectRatio(new Rational(16, 9));
                    var pipParams = builder.Build();
                    if (pipParams != null)
                        activity.EnterPictureInPictureMode(pipParams);
                }
            }
#endif
        }

        private void OnBrightnessPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _startBrightness = _currentBrightness;
                    BrightnessIndicator.IsVisible = true;
                    break;

                case GestureStatus.Running:
                    double delta = -e.TotalY / 200.0;
                    _currentBrightness = Math.Clamp(_startBrightness + delta, 0, 1);
                    BrightnessBar.Progress = _currentBrightness;
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        MainThread.BeginInvokeOnMainThread(() =>
                            BrightnessIndicator.IsVisible = false);
                    });
                    break;
            }
        }
    }
}
