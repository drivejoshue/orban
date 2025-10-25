using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using OrbanaDrive.Services;
using OrbanaDrive.Views;
using System.Collections.ObjectModel;
using The49.Maui.BottomSheet;

namespace OrbanaDrive.ViewModels;

public partial class HomeVM : ObservableObject, IDisposable
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    public HomeVM(ApiService api, SessionService session)
    {
        _api = api;
        _session = session;
    }

    // ---------- Estado UI ----------
    [ObservableProperty] private string driverStatus = "Offline";   // Offline | Abierto
    [ObservableProperty] private string availability = "Busy";      // Disponible | Busy
    [ObservableProperty] private string locationText = "—";
    [ObservableProperty] private bool isShiftOpen;
    [ObservableProperty] private int? shiftId;

    [ObservableProperty] private string? shiftLine1;   // "Turno #27 · 15/10 00:19"
    [ObservableProperty] private string? shiftLine2;   // "01615 (YZX-123B)"
    [ObservableProperty] private string? vehicleInfo;  // igual a shiftLine2 (útil si lo necesitas)
    [ObservableProperty] private string? avatarUrl;

  

    public ObservableCollection<ApiService.OfferItemDto> Offers { get; } = new();

    System.Timers.Timer? _pingTimer;
    System.Timers.Timer? _offersTimer;
    private bool _changingAvailability; // evita recursión al setear IsAvailable desde código

    // =========================================================
    // Init / Sesión
    // =========================================================
    [RelayCommand]
    public async Task InitAsync()
    {
        var me = await _api.GetSessionAsync();
        _session.Set(me);

        var sh = me?.current_shift;
        var isOpen = (sh?.id ?? 0) > 0 &&
                     (string.Equals(sh?.status, "abierto", StringComparison.OrdinalIgnoreCase) ||
                      string.IsNullOrWhiteSpace(sh?.ended_at));

        if (me?.driver == null)
        {
            ResetUiNoDriver();
            return;
        }

        if (isOpen)
        {
            shiftId = sh!.id;
            IsShiftOpen = true;
            DriverStatus = "Abierto";

            var eco = me.vehicle?.economico ?? me.vehicle?.model ?? "Vehículo";
            var plate = me.vehicle?.plate;
            VehicleInfo = string.IsNullOrWhiteSpace(plate) ? eco : $"{eco} ({plate})";

            // Turno #id · dd/MM HH:mm
            string started = "";
            if (!string.IsNullOrWhiteSpace(sh.started_at) &&
                DateTime.TryParse(sh.started_at, out var dt))
                started = $" · {dt:dd/MM HH:mm}";

            ShiftLine1 = $"Turno #{sh.id}{started}";
            ShiftLine2 = VehicleInfo;

            // Avatar (si agregaste foto_path al DTO del driver)
            avatarUrl = null;
            var foto = me.driver?.foto_path; // <-- requiere propiedad en DTO
            if (!string.IsNullOrWhiteSpace(foto))
                AvatarUrl = $"{_api.BaseUrl.TrimEnd('/')}/{foto.TrimStart('/')}";

            Availability = IsAvailable ? "Disponible" : "Busy";
            if (IsAvailable) { StartPings(); StartOffersPoll(); }
            else { StopPings(); StopOffersPoll(); }


            var api = Helpers.ServiceHelper.Get<ApiService>();
            var active = await api.GetActiveRideAsync();
            if (active is not null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var stack = Shell.Current.Navigation.NavigationStack;
                    if (!stack.OfType<OrbanaDrive.Views.RidePage>().Any())
                        await Shell.Current.Navigation.PushAsync(new OrbanaDrive.Views.RidePage(active));
                });
            }

        }
        else
        {
            ResetUiNoShift();
        }
    }

    [RelayCommand]
    public async Task ShowMeRawAsync()
    {
        var raw = await _api.GetSessionRawAsync();
        await ShowSheet("/api/auth/me", raw);
    }

    // =========================================================
    // Turno
    // =========================================================
    [RelayCommand]
    public async Task StartShiftAsync()
    {
        if (IsShiftOpen) return;

        var (ok, id, msg) = await _api.StartShiftAsync();
        if (!ok || id is null)
        {
            await ShowSheet("Turno", msg ?? "No se pudo iniciar turno");
            return;
        }

        // Refrescar datos
        await InitAsync();

        // Al abrir turno arrancamos Busy
        _changingAvailability = true;
        IsAvailable = false;
        _changingAvailability = false;
        Availability = "Busy";
        StopPings(); StopOffersPoll();
    }

    [RelayCommand]
    public async Task FinishShiftAsync()
    {
        if (!IsShiftOpen) return;

        var (ok, msg) = await _api.FinishShiftAsync(ShiftId);
        if (!ok)
        {
            await ShowSheet("Turno", msg ?? "No se pudo cerrar turno");
            return;
        }

        StopPings(); StopOffersPoll();
        ResetUiNoShift();

        // Refrescar
        await InitAsync();
    }

    // =========================================================
    // Disponible / Busy (sin code-behind)
    // =========================================================
    // Hook auto-generado por el Toolkit cuando cambia IsAvailable

    [ObservableProperty] private bool isAvailable;
  

    // Se ejecuta cada que cambia IsAvailable (generado por [ObservableProperty])
    partial void OnIsAvailableChanged(bool value)
    {
        if (_changingAvailability) return;             // evita recursión si vino del comando
        _ = ApplyAvailabilityAsync(value);             // fire-and-forget (no bloquear UI)
    }
    // true=Disponible (hook abajo)
    [RelayCommand]
    public Task ToggleAvailabilityAsync()
    {
        return ApplyAvailabilityFromCommand();
    }

    private Task ApplyAvailabilityFromCommand()
    {
        var next = !IsAvailable;           // valor destino
        _changingAvailability = true;
        IsAvailable = next;                // dispara el hook, pero anulado por el flag
        _changingAvailability = false;
        return ApplyAvailabilityAsync(next);
    }

    // Lógica real (API + pings + label)
    private async Task ApplyAvailabilityAsync(bool value)
    {
        if (!IsShiftOpen)
        {
            await ShowSheet("Turno", "Abre turno primero");
            // revierte visual si no hay turno
            _changingAvailability = true;
            IsAvailable = false;
            _changingAvailability = false;
            Availability = "Busy";
            return;
        }

        Availability = value ? "Disponible" : "Busy";

        var (lat, lng) = await GetCurrentLL();
        await _api.SendLocationAsync(lat, lng, busy: !value);
        LocationText = $"{lat:F5}, {lng:F5}";

        if (value) { StartPings(); StartOffersPoll(); }
        else { StopPings(); StopOffersPoll(); }
    }

    [RelayCommand]
    public async Task SendLocationOnceAsync()
    {
        var (lat, lng) = await GetCurrentLL();
        if (await _api.SendLocationAsync(lat, lng))
            LocationText = $"{lat:F5}, {lng:F5}";
    }

    // =========================================================
    // Ofertas (polling cuando Disponible)
    // =========================================================
    private void StartOffersPoll()
    {
        _offersTimer ??= new System.Timers.Timer(5_000);
        _offersTimer.Elapsed -= OnOffersTick;
        _offersTimer.Elapsed += OnOffersTick;
        _offersTimer.AutoReset = true;
        _offersTimer.Start();
    }

    private void StopOffersPoll()
    {
        if (_offersTimer is null) return;
        _offersTimer.Stop();
        _offersTimer.Elapsed -= OnOffersTick;
    }

    private async void OnOffersTick(object? s, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var list = await _api.GetOffersAsync("offered");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Offers.Clear();
                foreach (var o in list) Offers.Add(o);
            });
        }
        catch { }
    }

    // Aceptar / Rechazar (por si los llamas desde otra vista)
    [RelayCommand]
    public async Task AcceptOfferAsync(long offerId)
    {
        if (!await _api.AcceptOfferAsync(offerId))
        {
            await ShowSheet("Ofertas", "No se pudo aceptar la oferta");
            return;
        }
        var list = await _api.GetOffersAsync("offered");
        Offers.Clear();
        foreach (var o in list) Offers.Add(o);
    }

    [RelayCommand]
    public async Task RejectOfferAsync(long offerId)
    {
        if (!await _api.RejectOfferAsync(offerId))
        {
            await ShowSheet("Ofertas", "No se pudo rechazar la oferta");
            return;
        }
        var list = await _api.GetOffersAsync("offered");
        Offers.Clear();
        foreach (var o in list) Offers.Add(o);
    }

    // =========================================================
    // Navegación
    // =========================================================
    
    [RelayCommand]
   

 
    public Task NavigateOffersAsync() => Shell.Current.GoToAsync(nameof(Views.OffersPage));

    [RelayCommand]
    public Task NavigateRideAsync() => Shell.Current.GoToAsync(nameof(Views.RidePage));

    [RelayCommand]
    public Task NavigateStandsAsync() => Shell.Current.GoToAsync(nameof(Views.StandsPage));

    [RelayCommand]
    public Task NavigateSettingsAsync() => Shell.Current.GoToAsync(nameof(Views.SettingsPage));
    // =========================================================
    // Helpers
    // =========================================================
    private void StartPings()
    {
        _pingTimer ??= new System.Timers.Timer(12_000);
        _pingTimer.Elapsed -= OnPing;
        _pingTimer.Elapsed += OnPing;
        _pingTimer.AutoReset = true;
        _pingTimer.Start();
    }

    private void StopPings()
    {
        if (_pingTimer is null) return;
        _pingTimer.Stop();
        _pingTimer.Elapsed -= OnPing;
    }

    private async void OnPing(object? s, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var (lat, lng) = await GetCurrentLL();
            var ok = await _api.SendLocationAsync(lat, lng);
            if (ok) MainThread.BeginInvokeOnMainThread(() =>
                LocationText = $"{lat:F5}, {lng:F5}");
        }
        catch { }
    }

    private static async Task<(double lat, double lng)> GetCurrentLL()
    {
        try
        {
            var r = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5)));
            if (r != null) return (r.Latitude, r.Longitude);
        }
        catch { }
        return (0, 0);
    }

    private void ResetUiNoDriver()
    {
        IsShiftOpen = false; ShiftId = null;
        DriverStatus = "Offline";
        _changingAvailability = true; IsAvailable = false; _changingAvailability = false;
        Availability = "Busy";
        ShiftLine1 = ShiftLine2 = VehicleInfo = AvatarUrl = null;
        LocationText = "—";
        StopPings(); StopOffersPoll();
    }

    private void ResetUiNoShift()
    {
        IsShiftOpen = false; ShiftId = null;
        DriverStatus = "Offline";
        _changingAvailability = true; IsAvailable = false; _changingAvailability = false;
        Availability = "Busy";
        ShiftLine1 = ShiftLine2 = VehicleInfo = AvatarUrl = null;
        LocationText = "—";
        StopPings(); StopOffersPoll();
    }

    // ------- Menú bottom sheet (igual que tenías) -------
    private BottomSheet? _menuSheet;
    private bool _menuOpen;
    private bool _menuTransitioning;

    [RelayCommand]
    public async Task ToggleMenuAsync()
    {
        if (_menuTransitioning) return;

        if (_menuOpen && _menuSheet is not null)
        {
            _menuTransitioning = true;
            try { await _menuSheet.DismissAsync(); }
            finally { _menuTransitioning = false; }
            return;
        }

        if (_menuSheet is not null)
        {
            _menuTransitioning = true;
            try { await _menuSheet.DismissAsync(); }
            catch { }
            finally { _menuSheet = null; _menuOpen = false; _menuTransitioning = false; }
        }

        _menuSheet = BuildMenuSheet();
        _menuSheet.Dismissed += (_, __) => { _menuOpen = false; _menuSheet = null; };
        _menuOpen = true;

        _menuTransitioning = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () => await _menuSheet!.ShowAsync());
        }
        finally { _menuTransitioning = false; }
    }

    private BottomSheet BuildMenuSheet()
    {
        var sheet = new BottomSheet { HasBackdrop = true, IsCancelable = true };
        sheet.Detents.Clear();
        sheet.Detents.Add(new MediumDetent());

        var btnLogout = new Button
        {
            Text = "Cerrar sesión",
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.OrangeRed
        };
        btnLogout.Clicked += async (_, __) =>
        {
            try
            {
                await _api.LogoutAsync();
                _session.Set(null);
                if (_menuSheet is not null) await _menuSheet.DismissAsync();
                await Shell.Current.GoToAsync("//login");
            }
            catch { }
        };

        sheet.Content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 16),
            Spacing = 10,
            Children =
            {
                new Label { Text = "Menú", FontSize = 18, FontAttributes = FontAttributes.Bold },
                new Button { Text = "Ajustes (próximamente)", BackgroundColor = Colors.Transparent, TextColor = Colors.White },
                new Button { Text = "Wallet (próximamente)",  BackgroundColor = Colors.Transparent, TextColor = Colors.White },
                btnLogout
            }
        };
        return sheet;
    }

    private static async Task ShowSheet(string title, string message)
    {
        var sheet = new BottomSheet { HasBackdrop = true, IsCancelable = true };
        sheet.Detents.Clear();
        sheet.Detents.Add(new MediumDetent());
        sheet.Content = new VerticalStackLayout
        {
            Padding = 20,
            Spacing = 10,
            Children =
            {
                new Label { Text = title, FontSize=18, FontAttributes=FontAttributes.Bold },
                new Label { Text = message, FontSize=14 },
                new Button { Text = "OK", Command = new Command(async () => await sheet.DismissAsync()) }
            }
        };
        await MainThread.InvokeOnMainThreadAsync(async () => await sheet.ShowAsync());
    }

    public void Dispose()
    {
        StopPings();
        StopOffersPoll();
        _pingTimer?.Dispose();
        _offersTimer?.Dispose();
    }
}
