using OrbanaDrive.Views;
namespace OrbanaDrive
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("login", typeof(Views.LoginPage));

            // Rutas para navegación
            Routing.RegisterRoute(nameof(Views.OffersPage), typeof(Views.OffersPage));
            Routing.RegisterRoute(nameof(Views.RidePage), typeof(Views.RidePage));
            Routing.RegisterRoute(nameof(Views.StandsPage), typeof(Views.StandsPage));
            Routing.RegisterRoute(nameof(Views.SettingsPage), typeof(Views.SettingsPage));

        }
    }
}
