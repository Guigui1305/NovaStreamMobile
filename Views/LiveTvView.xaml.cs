using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.ViewModels;
using System;
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

        public LiveTvView()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _vm = BindingContext as LiveTvViewModel;
            AttachVideoView();
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
                nativeView.Content = new Label { Text = "" }; // placeholder

                // Use handler to embed native Android view
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

            // Reset selection for re-tap
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
                ContentGrid.IsVisible = false;
            }
            else
            {
                Shell.SetTabBarIsVisible(this, true);
                Shell.SetNavBarIsVisible(this, true);
                PlayerRow.Height = new GridLength(250);
                SearchRow.Height = GridLength.Auto;
                ContentGrid.IsVisible = true;
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
                    {
                        activity.EnterPictureInPictureMode(pipParams);
                    }
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
                        {
                            BrightnessIndicator.IsVisible = false;
                        });
                    });
                    break;
            }
        }
    }
}
