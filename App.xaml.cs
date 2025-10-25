using OrbanaDrive.Services;

namespace OrbanaDrive;

public partial class App : Application
{
    readonly SessionService _session;
    readonly SettingsService _settings;
    readonly ApiService _api;

    public App(SessionService session, SettingsService settings, ApiService api)
    {
        InitializeComponent();
        _session = session;
        _settings = settings;
        _api = api;

        MainPage = new AppShell();
        _ = DecideStartAsync();
    }

    async Task DecideStartAsync()
    {
        var token = await _settings.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        // Pegarle a /auth/me y guardar en SessionService
        var me = await _api.GetSessionAsync();
        if (me == null)
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        _session.Set(me);

        // Si quieres reaccionar al turno abierto desde Home, basta con leer SessionService.HasOpenShift
        await Shell.Current.GoToAsync("//home");
    }
}
