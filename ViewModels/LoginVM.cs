using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using OrbanaDrive.Services;
using The49.Maui.BottomSheet;

namespace OrbanaDrive.ViewModels;

public partial class LoginVM : ObservableObject
{
    private readonly ApiService _api;

    [ObservableProperty] private string email = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty]
    private bool rememberMe =
       Preferences.Get("login_remember", true);

    public LoginVM(ApiService api) => _api = api;

    [RelayCommand]
    public async Task DoLoginAsync()
    {
        if (IsBusy) return; IsBusy = true;
        try
        {
            var result = await _api.LoginAsync((Email ?? "").Trim(), Password ?? "");
            if (!result.Ok)
            {
                await ShowSheetAsync("Login", result.Message ?? "Error de autenticación");
                return;
            }

            // Persistencia básica del “recordarme”
            Preferences.Set("login_remember", RememberMe);
            if (RememberMe) Preferences.Set("login_email", Email ?? "");
            else Preferences.Remove("login_email");

            await Shell.Current.GoToAsync("//home");
        }
        finally { IsBusy = false; }
    }
    // BottomSheet estable (List<Detent> y sheet construido antes de usar en el Command)
    private static async Task ShowSheetAsync(string title, string message)
    {
        var sheet = new BottomSheet
        {
            Detents = new List<Detent> { new MediumDetent() }
        };

        sheet.Content = new VerticalStackLayout
        {
            Padding = 20,
            Spacing = 10,
            Children =
            {
                new Label {
                    Text = title, FontAttributes = FontAttributes.Bold, FontSize = 18,
                    TextColor = (Color)Application.Current.Resources["ClrTextPrimary"]
                },
                new Label {
                    Text = message, FontSize = 14,
                    TextColor = (Color)Application.Current.Resources["ClrTextSecondary"]
                },
                new Button {
                    Text = "OK",
                    Command = new Command(async () => await sheet.DismissAsync())
                }
            }
        };

        await sheet.ShowAsync();
    }
}
