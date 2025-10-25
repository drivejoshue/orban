// Views/RidePage.xaml.cs
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using OrbanaDrive.Services;
using The49.Maui.BottomSheet;

#if ANDROID
using Microsoft.Maui.Maps.Handlers;          // MapHandler
using Android.Gms.Maps;                      // MapView
using OrbanaDrive.Platforms.Android;         // MapReadyCallback
#endif

namespace OrbanaDrive.Views;

public partial class RidePage : ContentPage
{
    private readonly ApiService _api;
    private readonly ApiService.OfferItemDto _ctx;
    private readonly int _rideId;

    private readonly MapsService _maps = new();
    private string _currentStep = "assigned"; // assigned | arrived | onboard | finished

    public RidePage(ApiService.OfferItemDto offer)
    {
        InitializeComponent();
        _api = Helpers.ServiceHelper.Get<ApiService>();
        _ctx = offer;
        _rideId = (int)offer.ride_id;
        RideMap.AutomationId = "ride";
        // (4) quitar back y “ligerito” el navbar
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior { IsEnabled = false });
        this.BackgroundColor = Color.FromArgb("#0E0E0E"); // refuerzo dark
        // (opcional Android) hacer nav bar semitransparente en AppTheme
    }

#if ANDROID
private void HookRideMap()
{
    if (RideMap?.Handler is MapHandler handler &&
        handler.PlatformView is MapView mapView)
    {
        // esto dispara OnMapReady y dibuja PNG + polyline + tooltip
        mapView.GetMapAsync(new MapReadyCallback(RideMap, handler));
    }
}
#endif


    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Evita flash inicial
        RideMap.IsVisible = false;

        FillUi();

        // Asegura que el handler del Map exista (Android especialmente)
        await EnsureMapReadyAsync();

        // Region inicial: usa ubicacion del chofer si hay; si no, el origen
        var initialCenter = await TryGetDriverLocationOrOriginAsync();
        MapUtils.SetInitialRegion(RideMap, initialCenter, kmRadius: 1.0);

        // Al abrir mostramos PICKUP: mi posicion  origen
        await DrawPickupRouteAsync();

#if ANDROID
    HookRideMap();    // <-- importante: ahora sí dibuja los PNG/tooltip
#endif


        // Ya con todo puesto, aparece el mapa
        RideMap.IsVisible = true;

        SetStep("assigned");
    }

    // ------- UI -------
    private void FillUi()
    {
        OriginLabel.Text = _ctx.origin_label;
        DestLabel.Text = _ctx.dest_label;
        AmountLabel.Text = _ctx.quoted_amount.HasValue ? $"MXN{_ctx.quoted_amount.Value:0}" : "—";
        EtaLabel.Text = _ctx.eta_seconds.HasValue ? $"ETA {Math.Max(1, _ctx.eta_seconds.Value / 60)} min" : "ETA —";
    }

    private void SetStep(string step)
    {
        _currentStep = step;

        // Oculta todo por default
        ArrivedBtn.IsVisible = BoardBtn.IsVisible = FinishBtn.IsVisible = false;
        ArrivedBtn.IsEnabled = BoardBtn.IsEnabled = FinishBtn.IsEnabled = false;

        // Full width (por si no lo tenías ya)
        ArrivedBtn.HorizontalOptions = LayoutOptions.Fill;
        BoardBtn.HorizontalOptions = LayoutOptions.Fill;
        FinishBtn.HorizontalOptions = LayoutOptions.Fill;

        switch (step)
        {
            case "assigned":
                StatusChip.Text = "Asignado"; StatusChip.TextColor = Color.FromArgb("#E67E80");
                ArrivedBtn.IsVisible = ArrivedBtn.IsEnabled = true;
                break;

            case "arrived":
                StatusChip.Text = "Arribé"; StatusChip.TextColor = Color.FromArgb("#F39C12");
                BoardBtn.IsVisible = BoardBtn.IsEnabled = true;
                break;

            case "onboard":
                StatusChip.Text = "En ruta"; StatusChip.TextColor = Color.FromArgb("#2F6FED");
                FinishBtn.IsVisible = FinishBtn.IsEnabled = true;
                break;

            case "finished":
                StatusChip.Text = "Finalizado"; StatusChip.TextColor = Color.FromArgb("#27AE60");
                break;
        }
    }


    // ------- Acciones -------
    private async void OnArrivedClicked(object? sender, EventArgs e)
    {
        if (await _api.RideArrivedAsync(_rideId))
        {
            SetStep("arrived");
            // Seguimos mostrando ruta de PICKUP (por si se movi)
            await DrawPickupRouteAsync();
        }
    }

    private async void OnBoardClicked(object? sender, EventArgs e)
    {
        if (await _api.RideBoardAsync(_rideId))
        {
            SetStep("onboard");
            // Cambia a ruta ORIGEN  DESTINO
            await DrawDestinationRouteAsync();
        }
    }

    private async void OnFinishClicked(object? sender, EventArgs e)
    {
        if (await _api.RideFinishAsync(_rideId))
        {
            SetStep("finished");
            await DisplayAlert("Servicio", "¡Viaje finalizado!", "OK");
            await Navigation.PopAsync();
        }
    }

    // FAB: abrir Google Maps nativo
    private async void OnOpenInGoogleMapsClicked(object? sender, EventArgs e)
    {
        double lat, lng;
        if (_currentStep == "onboard")
        {
            lat = _ctx.dest_lat ?? _ctx.origin_lat;
            lng = _ctx.dest_lng ?? _ctx.origin_lng;
        }
        else
        {
            lat = _ctx.origin_lat;
            lng = _ctx.origin_lng;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var uri = new Uri($"https://www.google.com/maps/dir/?api=1&destination={lat.ToString(inv)},{lng.ToString(inv)}&travelmode=driving");
        await Launcher.TryOpenAsync(uri);
    }

    // ------- Rutas / Mapa -------

    // Espera breve hasta que el Map tenga handler (evita frames negros y africa)
    private async Task EnsureMapReadyAsync()
    {
        var tries = 0;
        while (RideMap.Handler is null && tries++ < 30)
            await Task.Delay(16); // un frame
    }

    // Usa: 1) GPS del dispositivo reciente, 2) me.driver.last_lat/lng, 3) origen
    private async Task<Location> TryGetDriverLocationOrOriginAsync()
    {
        try
        {
            var last = await Geolocation.GetLastKnownLocationAsync();
            if (last is not null && last.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-30))
                return new Location(last.Latitude, last.Longitude);
        }
        catch { /* permisos, etc. */ }

        var me = await _api.GetSessionAsync();
        if (double.TryParse(me?.driver?.last_lat, out var lat) &&
            double.TryParse(me?.driver?.last_lng, out var lng))
            return new Location(lat, lng);

        return new Location(_ctx.origin_lat, _ctx.origin_lng);
    }

    // Tu-> Origen
    private async Task DrawPickupRouteAsync()
    {
        var (meLat, meLng) = await GetCurrentLL();

        var a = (meLat, meLng);
        var b = (_ctx.origin_lat, _ctx.origin_lng);

        var route = await _api.GetRouteAsync(a.Item1, a.Item2, b.Item1, b.Item2)
                    ?? new List<(double, double)> { a, b };

        RideMap.Pins.Clear();
        RideMap.MapElements.Clear();

        RideMap.Pins.Add(new Pin { Label = "Tú", Location = new Location(a.Item1, a.Item2), Type = PinType.SavedPin });
        RideMap.Pins.Add(new Pin { Label = "A", Location = new Location(b.Item1, b.Item2), Type = PinType.Place });

        _maps.DrawRoute(RideMap, route);
        _maps.FitToPositions(RideMap, route);
#if ANDROID
    HookRideMap();   // muestra car_pin en “Tú” y passenger_pin en “A”
#endif
    }

    // Origen -> Destino
    private async Task DrawDestinationRouteAsync()
    {
        var a = (_ctx.origin_lat, _ctx.origin_lng);
        var b = (_ctx.dest_lat ?? _ctx.origin_lat, _ctx.dest_lng ?? _ctx.origin_lng);

        var route = await _api.GetRouteAsync(a.Item1, a.Item2, b.Item1, b.Item2)
                    ?? new List<(double, double)> { a, b };

        RideMap.Pins.Clear();
        RideMap.MapElements.Clear();

        RideMap.Pins.Add(new Pin { Label = "A", Location = new Location(a.Item1, a.Item2), Type = PinType.Place });
        RideMap.Pins.Add(new Pin { Label = "B", Location = new Location(b.Item1, b.Item2), Type = PinType.Place });

        _maps.DrawRoute(RideMap, route);
        _maps.FitToPositions(RideMap, route);
#if ANDROID
    HookRideMap();   // muestra car_pin en “Tú” y passenger_pin en “A”
#endif
    }

    // Fallback ubicacin
    private async Task<(double lat, double lng)> GetCurrentLL()
    {
        try
        {
            var last = await Geolocation.GetLastKnownLocationAsync();
            if (last != null) return (last.Latitude, last.Longitude);

            var req = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
            var loc = await Geolocation.GetLocationAsync(req);
            if (loc != null) return (loc.Latitude, loc.Longitude);
        }
        catch { }
        return (_ctx.origin_lat, _ctx.origin_lng);
    }


    // RidePage.xaml.cs (dentro de la clase)
    // Views/RidePage.xaml.cs  (agrega este handler completo)
    private async void OnCancelRideClicked(object? sender, EventArgs e)
    {
        try
        {
            // 1) Cargar motivos desde API (o defaults si falla)
            var reasons = await _api.GetCancelReasonsAsync();
            if (reasons == null || reasons.Count == 0)
                reasons = new List<string> { "Pasajero no responde", "Dirección incorrecta", "Esperó demasiado", "Emergencia del conductor", "Otro…" };

            // 2) ActionSheet para elegir motivo
            var opts = reasons.ToArray();
            var chosen = await DisplayActionSheet("Motivo de cancelación", "Cerrar", null, opts);
            if (string.IsNullOrWhiteSpace(chosen) || chosen == "Cerrar") return;

            // 2.1) Si elige “Otro…”, pedir texto
            if (chosen.Trim().Equals("Otro…", StringComparison.OrdinalIgnoreCase) ||
                chosen.Trim().Equals("Otro", StringComparison.OrdinalIgnoreCase))
            {
                var typed = await DisplayPromptAsync("Otro motivo", "Describe brevemente el motivo:", "OK", "Cancelar", maxLength: 160);
                if (string.IsNullOrWhiteSpace(typed)) return;
                chosen = typed.Trim();
            }

            // 3) Confirmación final
            var ok = await DisplayAlert("Cancelar servicio", "¿Estás seguro de cancelar este viaje?", "Sí, cancelar", "No");
            if (!ok) return;

            // 4) Llamar API
            var done = await _api.DriverCancelRideAsync(_rideId, chosen);
            if (!done)
            {
                await DisplayAlert("Error", "No se pudo cancelar el servicio. Inténtalo de nuevo.", "OK");
                return;
            }

            await DisplayAlert("Servicio", "El viaje ha sido cancelado.", "OK");

            // 5) Volver a Ofertas
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("cancel error: " + ex);
            await DisplayAlert("Error", "Ocurrió un problema al cancelar.", "OK");
        }
    }


}
