using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;
#if ANDROID
using Android.App;
using Android.Util;
#endif

namespace NovaStreamMobile.Views
{
    public partial class LiveTvView : ContentPage
    {
        private bool _isFullscreen = false;
        private double _currentBrightness = 0.5;
        private double _startBrightness;

        public LiveTvView()
        {
            InitializeComponent();
        }

        private void OnFullscreenClicked(object sender, EventArgs e)
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
                PlayerRow.Height = new GridLength(280);
                SearchRow.Height = GridLength.Auto;
                ContentGrid.IsVisible = true;
            }
        }

        private void OnPipClicked(object sender, EventArgs e)
        {
#if ANDROID
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity != null)
            {
                var builder = new PictureInPictureParams.Builder();
                // Set aspect ratio (16:9)
                builder.SetAspectRatio(new Rational(16, 9));
                activity.EnterPictureInPictureMode(builder.Build());
            }
#endif
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            // Handle UI adjustments when returning from PiP
        }

        private void OnBrightnessPanUpdated(object sender, PanUpdatedEventArgs e)
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
                    Task.Delay(1000).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => 
                    {
                        BrightnessIndicator.IsVisible = false;
                    }));
                    break;
            }
        }
    }
}
