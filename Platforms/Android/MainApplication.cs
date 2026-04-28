using Android.App;
using Android.Runtime;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace NovaStreamMobile
{
#if DEBUG
    [Application(UsesCleartextTraffic = true)]
#else
    [Application(UsesCleartextTraffic = true, NetworkSecurityConfig = "@xml/network_security_config")]
#endif
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
