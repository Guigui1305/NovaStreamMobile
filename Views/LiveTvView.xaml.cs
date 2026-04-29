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
using Android.Content;
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
        private bool _videoViewReady = false;
        private bool _attachingVideoView = false;

#if ANDROID
        private LibVLCSharp.Platforms.Android.VideoView? _androidVideoView;
#endif

        public LiveTvView()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                _vm = BindingContext as LiveTvViewModel;

                // Attacher la VideoView si pas encore fait
                if (!_videoViewReady && !_attachingVideoView)
                {
                    _attachingVideoView = true;
                    _ = AttachVideoViewAsync();
                }

                if (_vm != null && !_categoriesBuilt)
                {
                    _vm.LiveCategories.CollectionChanged += (s, e) =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try { BuildCategoryButtons(); }
                            catch { }
                        });
                    };
                    if (_vm.LiveCategories.Count > 0)
                        BuildCategoryButtons();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] OnAppearing error: {ex}");
            }
        }

        /// <summary>
        /// Methode publique pour lancer la lecture de maniere securisee.
        /// Attend que la VideoView soit prete avant de lancer la lecture.
        /// Appelee depuis HomeView et VodView apres navigation.
        /// </summary>
        public async Task PlayChannelSafeAsync(Channel channel)
        {
            try
            {
                _vm = BindingContext as LiveTvViewModel;
                if (_vm == null) return;

                // Attendre que la VideoView soit prete (max 5 secondes)
                for (int i = 0; i < 50; i++)
                {
                    if (_videoViewReady) break;
                    await Task.Delay(100);
                }

                // Delai supplementaire pour la surface Android
                await Task.Delay(800);

                // Lancer la lecture sur le MainThread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        _vm.SelectedChannel = channel;
                        UpdatePlayPauseButton();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LiveTvView] PlayChannelSafe error: {ex}");
                        // Fallback: ouvrir dans un lecteur externe
                        _ = OpenInExternalPlayerAsync(channel.Url, channel.Name);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] PlayChannelSafeAsync error: {ex}");
                // Fallback: ouvrir dans un lecteur externe
                await OpenInExternalPlayerAsync(channel.Url, channel.Name);
            }
        }

        private async Task AttachVideoViewAsync()
        {
            if (_vm?.MediaPlayer == null)
            {
                _videoViewReady = true; // Marquer comme pret pour eviter blocage
                _attachingVideoView = false;
                return;
            }

#if ANDROID
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity == null)
                {
                    _videoViewReady = true;
                    _attachingVideoView = false;
                    return;
                }

                // Creer la VideoView SANS assigner le MediaPlayer tout de suite
                _androidVideoView = new LibVLCSharp.Platforms.Android.VideoView(activity);

                // Creer un ContentView MAUI pour obtenir un handler natif
                var placeholder = new Microsoft.Maui.Controls.ContentView();
                placeholder.Content = new BoxView { Color = Colors.Black };
                VideoContainer.Content = placeholder;

                // Attendre que le handler MAUI soit pret (plusieurs cycles de layout)
                await Task.Delay(500);

                // Maintenant attacher la VideoView Android au container natif
                bool attached = false;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        attached = await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            try
                            {
                                var handler = VideoContainer.Handler;
                                if (handler?.PlatformView is Android.Views.ViewGroup container)
                                {
                                    // 1. D'abord nettoyer le container
                                    container.RemoveAllViews();

                                    // 2. Retirer la VideoView de son ancien parent si necessaire
                                    if (_androidVideoView.Parent is Android.Views.ViewGroup oldParent)
                                        oldParent.RemoveView(_androidVideoView);

                                    // 3. AJOUTER la VideoView au layout AVANT d'assigner le MediaPlayer
                                    container.AddView(_androidVideoView, new Android.Views.ViewGroup.LayoutParams(
                                        Android.Views.ViewGroup.LayoutParams.MatchParent,
                                        Android.Views.ViewGroup.LayoutParams.MatchParent));

                                    System.Diagnostics.Debug.WriteLine("[LiveTvView] VideoView added to container");
                                    return true;
                                }
                                return false;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LiveTvView] Attach attempt error: {ex}");
                                return false;
                            }
                        });

                        if (attached) break;
                    }
                    catch { }

                    await Task.Delay(200 * (attempt + 1));
                }

                if (attached)
                {
                    // 4. Attendre que la VideoView soit dans le window et ait une surface
                    await Task.Delay(500);

                    // 5. MAINTENANT assigner le MediaPlayer (APRES que la VideoView est dans le layout)
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try
                        {
                            _androidVideoView.MediaPlayer = _vm.MediaPlayer;
                            System.Diagnostics.Debug.WriteLine("[LiveTvView] MediaPlayer assigned to VideoView AFTER layout attachment");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LiveTvView] MediaPlayer assignment error: {ex}");
                        }
                    });

                    // 6. Attendre encore un peu pour la surface
                    await Task.Delay(300);
                }

                _videoViewReady = true;
                _attachingVideoView = false;
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] VideoView ready: attached={attached}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] AttachVideoView error: {ex}");
                _videoViewReady = true;
                _attachingVideoView = false;
            }
#else
            _videoViewReady = true;
            _attachingVideoView = false;
#endif
        }

        /// <summary>
        /// Ouvre le flux dans un lecteur externe (VLC, MX Player, etc.) comme fallback.
        /// </summary>
        private async Task OpenInExternalPlayerAsync(string url, string title)
        {
#if ANDROID
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity == null) return;

                var intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                intent.PutExtra("title", title);
                intent.AddFlags(ActivityFlags.NewTask);

                // Verifier qu'un lecteur externe est disponible
                if (intent.ResolveActivity(activity.PackageManager!) != null)
                {
                    activity.StartActivity(intent);
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Lecteur externe",
                            "Aucun lecteur video externe trouve. Installez VLC ou MX Player depuis le Play Store.",
                            "OK");
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] External player error: {ex}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Erreur", $"Impossible d'ouvrir le lecteur externe: {ex.Message}", "OK");
                });
            }
#endif
        }

        private void OnChannelSelected(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (e.CurrentSelection.Count == 0) return;
                var channel = e.CurrentSelection[0] as Channel;
                if (channel == null) return;

                if (_vm != null)
                {
                    if (!_videoViewReady)
                    {
                        _ = PlayChannelSafeAsync(channel);
                    }
                    else
                    {
                        try
                        {
                            _vm.SelectedChannel = channel;
                            UpdatePlayPauseButton();
                        }
                        catch (Exception playEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LiveTvView] Play error, trying external: {playEx}");
                            _ = OpenInExternalPlayerAsync(channel.Url, channel.Name);
                        }
                    }
                }

                if (sender is CollectionView cv)
                    cv.SelectedItem = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] OnChannelSelected error: {ex}");
            }
        }

        private void BuildCategoryButtons()
        {
            if (_vm == null) return;
            _categoriesBuilt = true;

            try
            {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] BuildCategoryButtons error: {ex}");
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
                try
                {
                    if (_vm != null)
                    {
                        _vm.SelectedCategoryId = categoryId;
                        BuildCategoryButtons();
                    }
                }
                catch { }
            };
            return btn;
        }

        private string CleanCategoryName(string name)
        {
            name = name.Replace("|FR|", "").Replace("|CH|", "").Replace("|MULTI|", "").Trim();
            if (name.StartsWith("*")) name = name.TrimStart('*').Trim();
            while (name.Length > 0 && !char.IsLetterOrDigit(name[0]) && name[0] != '(')
                name = name.Substring(1).Trim();
            return name;
        }

        private void UpdatePlayPauseButton()
        {
            try
            {
                if (_vm != null)
                    PlayPauseBtn.Text = _vm.IsPlaying ? "||" : ">";
            }
            catch { }
        }

        private async void OnSubtitleClicked(object? sender, EventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] OnSubtitleClicked error: {ex}");
            }
        }

        private void OnFullscreenClicked(object? sender, EventArgs e)
        {
            try
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
            catch { }
        }

        private void OnPipClicked(object? sender, EventArgs e)
        {
#if ANDROID
            try
            {
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveTvView] OnPipClicked error: {ex}");
            }
#endif
        }

        private void OnBrightnessPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            try
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
                            {
                                try { BrightnessIndicator.IsVisible = false; }
                                catch { }
                            });
                        });
                        break;
                }
            }
            catch { }
        }
    }
}
