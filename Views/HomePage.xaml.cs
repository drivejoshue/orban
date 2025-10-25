using OrbanaDrive.Helpers;
using OrbanaDrive.ViewModels;

namespace OrbanaDrive.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
        var vm = ServiceHelper.Get<HomeVM>();   // misma instancia (singleton en DI)
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await (BindingContext as HomeVM)!.InitAsync();
    }

    // Navegación simple por PushAsync (sin rutas)
    private async void OnOffersTapped(object sender, TappedEventArgs e)
    {
        var page = ServiceHelper.Get<OffersPage>();
        await Navigation.PushAsync(page);
    }

    private async void OnRideTapped(object sender, TappedEventArgs e)
    {
        var page = ServiceHelper.Get<RidePage>();
        await Navigation.PushAsync(page);
    }

    private async void OnStandsTapped(object sender, TappedEventArgs e)
    {
        var page = ServiceHelper.Get<StandsPage>();
        await Navigation.PushAsync(page);
    }

    private async void OnSettingsTapped(object sender, TappedEventArgs e)
    {
        var page = ServiceHelper.Get<SettingsPage>();
        await Navigation.PushAsync(page);
    }

    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        await (BindingContext as HomeVM)!.ToggleMenuAsync();
    }
}
