using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using Microsoft.Maui;

namespace NovaStreamMobile
{
    [Activity(Theme = "@style/Maui.MainTheme.NoActionBar", 
              MainLauncher = true, 
              ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
              SupportsPictureInPicture = true)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
        }

        protected override void OnUserLeaveHint()
        {
            base.OnUserLeaveHint();
            // This is called when the user presses the home button
            // We can trigger PiP mode here if a video is playing
        }
    }
}
