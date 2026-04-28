using NovaStreamMobile.Views;

namespace NovaStreamMobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Start with Profile Selection for a premium experience
            MainPage = new ProfileSelectionView();
        }
    }
}
