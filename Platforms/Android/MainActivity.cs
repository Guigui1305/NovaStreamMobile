using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using Android.Runtime;
using Microsoft.Maui;

namespace NovaStreamMobile
{
    [Activity(Theme = "@style/Maui.MainTheme.NoActionBar", 
              MainLauncher = true, 
              ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
              SupportsPictureInPicture = true)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Handler global pour les exceptions Java non gerees sur Android
            AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ANDROID CRASH] {args.Exception?.GetType().Name}: {args.Exception?.Message}\n{args.Exception?.StackTrace}");
                // Ne pas re-lancer l'exception pour eviter le crash
                args.Handled = true;
            };
        }

        protected override void OnUserLeaveHint()
        {
            base.OnUserLeaveHint();
        }
    }
}
