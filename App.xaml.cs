using NovaStreamMobile.Views;

namespace NovaStreamMobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            try
            {
                return new Window(new ProfileSelectionView());
            }
            catch (Exception ex)
            {
                // Fallback: show error on screen so we can debug
                var errorPage = new ContentPage
                {
                    BackgroundColor = Colors.Black,
                    Content = new ScrollView
                    {
                        Content = new Label
                        {
                            Text = $"STARTUP ERROR:\n\n{ex.GetType().Name}\n{ex.Message}\n\n{ex.StackTrace}",
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
