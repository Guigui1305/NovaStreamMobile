using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
using NovaStreamMobile.Views;

namespace NovaStreamMobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            EnsureDefaultSource();
        }

        private void EnsureDefaultSource()
        {
            try
            {
                var storage = new StorageService();
                var sources = storage.LoadSources();
                if (sources.Count == 0)
                {
                    sources.Add(new MediaSource
                    {
                        Name = "IPTV Premium",
                        Url = "http://4k.4k-26com.com:80",
                        Type = SourceType.Xtream,
                        Username = "ey7ipyefih",
                        Password = "ya7me0jekf"
                    });
                    storage.SaveSources(sources);
                }
            }
            catch { }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            try
            {
                return new Window(new ProfileSelectionView());
            }
            catch (Exception ex)
            {
                var errorPage = new ContentPage
                {
                    BackgroundColor = Colors.Black,
                    Content = new ScrollView
                    {
                        Content = new Label
                        {
                            Text = $"ERREUR AU DEMARRAGE:\n\n{ex.GetType().Name}\n{ex.Message}\n\n{ex.InnerException?.Message}\n\n{ex.StackTrace}",
                            TextColor = Colors.Red,
                            FontSize = 14,
                            Margin = new Thickness(20),
                            LineBreakMode = LineBreakMode.WordWrap
                        }
                    }
                };
                return new Window(errorPage);
            }
        }
    }
}
