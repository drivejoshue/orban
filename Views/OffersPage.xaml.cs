using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Dispatching;
using OrbanaDrive.Helpers;
using OrbanaDrive.Services;
using OrbanaDrive.ViewModels;
using Plugin.Maui.Audio;
using The49.Maui.BottomSheet;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Map = Microsoft.Maui.Controls.Maps.Map;
using Polyline = Microsoft.Maui.Controls.Maps.Polyline;


namespace OrbanaDrive.Views;

public partial class OffersPage : ContentPage
{
    private readonly OffersVM _vm;
    private readonly ApiService _api;
    bool _waveOpen;
    long? _waveOpenOfferId;


    IDispatcherTimer? _timer;

    // control de directas
    readonly Queue<ApiService.OfferItemDto> _directQueue = new();
    readonly HashSet<long> _queuedDirectIds = new();
    bool _directOpen;
    bool _refreshing; // evita reentradas de refresh

    readonly IAudioManager _audio;
    IAudioPlayer? _bellPlayer;
    IAudioPlayer? _wavePlayer;
    IDispatcherTimer? _countdownTimer;
    DateTimeOffset? _expiresAtCurrent;
    ApiService.OfferItemDto? _offerShown;

    private readonly HashSet<long> _waveSeen = new();     // ids ya vistos (ola)
    private readonly HashSet<long> _waveChimed = new();   // ids que ya sonaron (ola)

    public OffersPage()
    {
        InitializeComponent();
        _vm = ServiceHelper.Get<OffersVM>();
        _api = ServiceHelper.Get<ApiService>();
      
        BindingContext = _vm;

        Shell.SetBackButtonBehavior(this, new BackButtonBehavior { IsEnabled = true });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await SyncAvailabilityAsync();
        if (_vm.IsAvailable) StartPolling();
        await RefreshAndShowDirectAsync();
       
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopPolling();
    }

    // --- disponibilidad desde /me ---
    private async Task SyncAvailabilityAsync()
    {
        try
        {
            var me = await _api.GetSessionAsync();
            var status = me?.driver?.status?.ToLowerInvariant();
            var isAvail = status is "idle" or "available";
            _vm.IsAvailable = isAvail;
            _vm.Availability = isAvail ? "Disponible" : "Busy";
        }
        catch { /* swallow */ }
    }

    // --- polling ---
    void StartPolling()
    {
        StopPolling();
        if (!_vm.IsAvailable) return;

        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    void StopPolling()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }


    async Task StartBellAsync()
    {
        try
        {
            var stream = await FileSystem.OpenAppPackageFileAsync("bell.mp3"); //  SIN ruta
            _bellPlayer = AudioManager.Current.CreatePlayer(stream);           // o _audio.CreatePlayer(stream)
            _bellPlayer.Loop = true;
            _bellPlayer.Volume = 1.0; // por si suena muy bajo
            _bellPlayer.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AUDIO] No se pudo reproducir bell.mp3: {ex.Message}");
        }
    }

    private async Task PlayWaveChimeAsync()
    {
        try
        {
            var stream = await FileSystem.OpenAppPackageFileAsync("bubble.mp3"); // <-- sin carpeta
            _wavePlayer?.Stop();
            _wavePlayer?.Dispose();

            _wavePlayer = AudioManager.Current.CreatePlayer(stream);
            _wavePlayer.Loop = false;
            _wavePlayer.Volume = 1.0;   // por si suena bajo
            _wavePlayer.Play();

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AUDIO] bubble.mp3 no se pudo reproducir: {ex.Message}");

        }
    }


    void StopBell()
    {
        try { _bellPlayer?.Stop(); _bellPlayer?.Dispose(); _bellPlayer = null; } catch { }
    }
    static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "—";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return $"****{digits}";
        var last4 = digits[^4..];
        return $"{new string('*', Math.Max(0, digits.Length - 4))}{last4}";
    }


    async void OnTick(object? s, EventArgs e)
    {
        // evita refrescar mientras un popup directo está abierto
        if (_vm.IsAvailable && !_directOpen)
        {
            try { await RefreshAndShowDirectAsync(); } catch { }
        }
    }

    // --- fetch + manejo de directas ---
    private static DateTimeOffset? ParseExpires(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTimeOffset.TryParse(s, out var dto)) return dto;
        if (DateTime.TryParse(s, out var dt)) return new DateTimeOffset(dt);
        return null;
    }

    private async Task RefreshAndShowDirectAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            var list = await _api.GetOffersAsync("offered") ?? new List<ApiService.OfferItemDto>();
            var now = DateTimeOffset.UtcNow;

            bool IsAlive(ApiService.OfferItemDto o)
            {
                var exp = ParseExpires(o.expires_at);
                // tolerancia mínima para evitar parpadeos por reloj
                return exp is null || exp.Value.ToUniversalTime() > now.AddSeconds(-1);
            }

            // -------- OLA (lista) --------
            var wave = list.Where(o => o.is_direct == 0 && IsAlive(o))
                           .OrderByDescending(o => o.offer_id)
                           .ToList();

            // detecta cuáles son nuevas para sonar una sola vez
            var newWave = new List<ApiService.OfferItemDto>();
            foreach (var w in wave)
                if (_waveSeen.Add(w.offer_id)) newWave.Add(w);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _vm.Offers.Clear();
                foreach (var o in wave) _vm.Offers.Add(o);
            });

            foreach (var w in newWave)
                if (_waveChimed.Add(w.offer_id))
                    await PlayWaveChimeAsync();

            // -------- DIRECTAS (popup en cola, sin duplicar) --------
            foreach (var d in list.Where(o => o.is_direct == 1 && IsAlive(o)))
                if (_queuedDirectIds.Add(d.offer_id))
                    _directQueue.Enqueue(d);

            // si no hay popup abierto, muestra la siguiente directa
            if (!_directOpen && _directQueue.Count > 0)
                await ShowNextDirectAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    async Task ShowNextDirectAsync()
    {
        if (_directOpen || _directQueue.Count == 0) return;

        var offer = _directQueue.Dequeue();
        _directOpen = true;

        // pausa polling mientras el popup esté visible
        await StartBellAsync();
        StopPolling();

        // por si había un contador previo
        StopCountdown();

        var sheet = BuildDirectOfferSheet(offer);

        // al cerrar: marcar cerrado, parar sonidos/contador, reanudar polling y continuar cola
        sheet.Dismissed += async (_, __) =>
        {
            _directOpen = false;
            StopBell();
            StopCountdown();

            if (_vm.IsAvailable) StartPolling();

            if (_directQueue.Count > 0)
                await ShowNextDirectAsync();
        };

        await MainThread.InvokeOnMainThreadAsync(async () => await sheet.ShowAsync());
    }


  
    // =============== DIRECTA (popup con minimapa + countdown) ==================
    private The49.Maui.BottomSheet.BottomSheet BuildDirectOfferSheet(ApiService.OfferItemDto o)
    {
        var sheet = new The49.Maui.BottomSheet.BottomSheet
        {
            HasBackdrop = true,
            IsCancelable = true
        };
        sheet.Detents.Clear();
        sheet.Detents.Add(new FullscreenDetent());

        // --- Mini-mapa (directa: mostramos A->B) ---
        var map = new Map
        {
            HeightRequest = 260,
            MapType = MapType.Street,
            IsTrafficEnabled = false
        };

        double aLat = o.origin_lat;
        double aLng = o.origin_lng;
        double bLat = o.dest_lat ?? o.origin_lat;
        double bLng = o.dest_lng ?? o.origin_lng;

        var A = new Location(aLat, aLng);
        var B = new Location(bLat, bLng);

        // Región inicial inmediata (evita África)
        map.MoveToRegion(MapSpan.FromCenterAndRadius(A, Distance.FromKilometers(1)));

        // Pins A/B (el PNG real lo setea el callback nativo)
        map.Pins.Add(new Pin { Label = "A", Address = o.origin_label, Location = A, Type = PinType.SavedPin });
        map.Pins.Add(new Pin { Label = "B", Address = o.dest_label, Location = B, Type = PinType.SavedPin });

        // Card con radios que recorta el mapa
        var mapCard = new Frame
        {
            Padding = 0,
            Margin = new Thickness(0, 4, 0, 8),
            CornerRadius = 16,
            HasShadow = false,
            BackgroundColor = Colors.Black,
            Content = map
        };

        // --- Dibujar ruta cuando el Map YA tenga handler ---
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await EnsureMapReadyFor(map); // <- clave para evitar NullReference

                var route = await _api.GetRouteAsync(A.Latitude, A.Longitude, B.Latitude, B.Longitude)
                            ?? new List<(double, double)> { (A.Latitude, A.Longitude), (B.Latitude, B.Longitude) };

                map.MapElements.Clear();
                var poly = new Polyline { StrokeWidth = 4 };
                foreach (var (lat, lng) in route)
                    poly.Geopath.Add(new Location(lat, lng));
                map.MapElements.Add(poly);

                FitBoundsAB(map, A, B, 1.8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DirectOffer route] " + ex);
                // fallback: ya centramos en A
            }
        });

        // -------- Helpers UI --------
        Label Title(string t) => new() { Text = t, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };

        Grid RowAB(string tag, string? text, Color tagBg)
        {
            var grid = new Grid { ColumnDefinitions = new() { new ColumnDefinition { Width = 28 }, new ColumnDefinition { Width = GridLength.Star } }, ColumnSpacing = 8 };
            var bubble = new Border
            {
                Stroke = Colors.Transparent,
                Background = tagBg,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                WidthRequest = 28,
                HeightRequest = 28,
                Content = new Label { Text = tag, TextColor = Colors.White, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
            };
            var lbl = new Label { Text = text ?? "-", TextColor = Colors.White, FontSize = 14 };
            grid.Children.Add(bubble); Grid.SetColumn(bubble, 0);
            grid.Children.Add(lbl); Grid.SetColumn(lbl, 1);
            return grid;
        }

        Border Chip(string t) => new()
        {
            Stroke = Colors.Gray,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(8, 4),
            Background = new SolidColorBrush(Color.FromArgb("#232323")),
            Content = new Label { Text = t, FontSize = 12, TextColor = Colors.LightGray }
        };

        // ---- CHIP DE COUNTDOWN ----
        var countdownLabel = new Label { Text = "—", FontSize = 12, TextColor = Colors.LightGray, VerticalTextAlignment = TextAlignment.Center };
        var chipCountdown = new Border
        {
            Stroke = Colors.Gray,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(8, 4),
            Background = new SolidColorBrush(Color.FromArgb("#232323")),
            Content = countdownLabel
        };

        var chips = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
        {
            Chip($"Dist: {(o.distance_m ?? o.ride_distance_m ?? 0)/1000.0:0.0} km"),
            Chip($"ETA: {Math.Max(1,(o.eta_seconds ?? 0)/60)} min"),
            Chip($"MXN {(o.quoted_amount ?? 0):0}"),
            chipCountdown // <- countdown visible
        }
        };

        var btnAccept = new Button
        {
            Text = $"Aceptar por MXN{(o.quoted_amount ?? 0):0}",
            BackgroundColor = Color.FromArgb("#2F6FED"),
            TextColor = Colors.White,
            CornerRadius = 14,
            Padding = new Thickness(16, 12),
            HeightRequest = 52,
            HorizontalOptions = LayoutOptions.Fill
        };
        btnAccept.Clicked += async (_, __) =>
        {
            try
            {
                if (await _api.AcceptOfferAsync(o.offer_id))
                {
                    await sheet.DismissAsync();
                    await Shell.Current.Navigation.PushAsync(new RidePage(o));
                }
                else
                {
                    await sheet.DismissAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Accept offer] " + ex);
            }
        };

        // Inicia countdown si tenemos expires_at
        _expiresAtCurrent = ParseExpires(o.expires_at);
        StartCountdown(countdownLabel, sheet, o);

        sheet.Content = new VerticalStackLayout
        {
            Padding = 16,
            Spacing = 12,
            Children =
        {
            Title("Solicitud de viaje"),
            mapCard,
            RowAB("A", o.origin_label, Color.FromArgb("#2F6FED")),
            RowAB("B", o.dest_label,  Color.FromArgb("#2ECC71")),
            chips,
            btnAccept
        }
        };

        return sheet;
    }



    // =============== OLA (popup con minimapa) ==================
    private async Task<The49.Maui.BottomSheet.BottomSheet> BuildWaveOfferSheetAsync(ApiService.OfferItemDto o)
    {
        var sheet = new The49.Maui.BottomSheet.BottomSheet
        {
            HasBackdrop = true,
            IsCancelable = true
        };
        sheet.Detents.Clear();
        sheet.Detents.Add(new FullscreenDetent());

        // --- Mini-mapa con radio y sin flash en África ---
        var map = new Map { HeightRequest = 260, MapType = MapType.Street, IsTrafficEnabled = false };

        // Normalizamos coords por si destino viene nulo
        double aLat = o.origin_lat;
        double aLng = o.origin_lng;
        double bLat = o.dest_lat ?? o.origin_lat;
        double bLng = o.dest_lng ?? o.origin_lng;

        var a = new Location(aLat, aLng);
        var b = new Location(bLat, bLng);

        // Evita “África”: centra de entrada en origen
        map.MoveToRegion(MapSpan.FromCenterAndRadius(a, Distance.FromKilometers(1)));

        // Pins básicos (los PNG los renderiza el callback nativo)
        map.Pins.Add(new Pin { Label = "A", Address = o.origin_label, Location = a, Type = PinType.SavedPin });
        map.Pins.Add(new Pin { Label = "B", Address = o.dest_label, Location = b, Type = PinType.SavedPin });

        // contenedor redondeado para el minimapa
        var mapCard = new Border
        {
            Stroke = Color.FromArgb("#2D2D2D"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Background = new SolidColorBrush(Color.FromArgb("#101010")),
            Padding = 0,
            Content = map
        };

        // --- Dibujar ruta y ajustar bounds cuando el map YA tiene handler ---
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await EnsureMapReadyFor(map); // <- clave para evitar NRE

                var route = await _api.GetRouteAsync(a.Latitude, a.Longitude, b.Latitude, b.Longitude)
                            ?? new List<(double, double)> { (a.Latitude, a.Longitude), (b.Latitude, b.Longitude) };

                // limpia y agrega polyline
                map.MapElements.Clear();
                var poly = new Polyline { StrokeWidth = 4 };
                foreach (var (lat, lng) in route)
                    poly.Geopath.Add(new Location(lat, lng));
                map.MapElements.Add(poly);

                FitBoundsAB(map, a, b, 1.8); // encuadre con padding
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[WaveSheet route] " + ex);
                // fallback: ya quedó centrado en A
            }
        });

        // --- UI auxiliar ---
        Label Title(string t) => new()
        {
            Text = t,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };

        Grid RowAB(string tag, string? text, Color tagBg)
        {
            var grid = new Grid { ColumnDefinitions = new() { new ColumnDefinition { Width = 28 }, new ColumnDefinition { Width = GridLength.Star } }, ColumnSpacing = 8 };
            var bubble = new Border
            {
                Stroke = Colors.Transparent,
                Background = tagBg,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                WidthRequest = 28,
                HeightRequest = 28,
                Content = new Label { Text = tag, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, TextColor = Colors.White, FontAttributes = FontAttributes.Bold }
            };
            var lbl = new Label { Text = text ?? "-", TextColor = Colors.White, FontSize = 14 };
            grid.Children.Add(bubble); Grid.SetColumn(bubble, 0);
            grid.Children.Add(lbl); Grid.SetColumn(lbl, 1);
            return grid;
        }

        Border Chip(string t) => new()
        {
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(10, 6),
            Background = new SolidColorBrush(Color.FromArgb("#1F2A44")),
            Content = new Label { Text = t, FontSize = 12, TextColor = Color.FromArgb("#BFD4FF") }
        };

        var chips = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
        {
            Chip($"Dist: {(o.distance_m ?? o.ride_distance_m ?? 0)/1000.0:0.0} km"),
            Chip($"ETA: {Math.Max(1,(o.eta_seconds ?? 0)/60)} min"),
            Chip($"MXN {(o.quoted_amount ?? 0):0}")
        }
        };

        var btnAccept = new Button
        {
            Text = $"Aceptar por MXN{(o.quoted_amount ?? 0):0}",
            BackgroundColor = Color.FromArgb("#2F6FED"),
            TextColor = Colors.White,
            CornerRadius = 16,
            Padding = new Thickness(18, 14),
            HorizontalOptions = LayoutOptions.Fill
        };
        var btnClose = new Button
        {
            Text = "Cerrar",
            BackgroundColor = Color.FromArgb("#333333"),
            TextColor = Colors.White,
            CornerRadius = 16,
            Padding = new Thickness(18, 14),
            HorizontalOptions = LayoutOptions.Fill
        };

        btnClose.Clicked += async (_, __) => await sheet.DismissAsync();
        btnAccept.Clicked += async (_, __) =>
        {
            try
            {
                if (await _api.AcceptOfferAsync(o.offer_id))
                {
                    await sheet.DismissAsync();
                    await Shell.Current.Navigation.PushAsync(new RidePage(o));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Accept offer] " + ex);
            }
        };

        var contentCard = new Border
        {
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Background = new SolidColorBrush(Color.FromArgb("#0F0F10")),
            Padding = 16,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
            {
                Title("Solicitud de viaje"),
                mapCard,
                RowAB("A", o.origin_label, Color.FromArgb("#2F6FED")),
                RowAB("B", o.dest_label,  Color.FromArgb("#2ECC71")),
                chips,
                btnAccept,
                btnClose
            }
            }
        };

        sheet.Content = new Grid { Padding = 12, Children = { contentCard } };
        return sheet;
    }



    // Espera hasta que un Map tenga handler (evita dibujar antes de tiempo)
    private static async Task EnsureMapReadyFor(Map map)
    {
        int tries = 0;
        while (map?.Handler is null && tries++ < 90)
            await Task.Delay(16);
    }

    private static void FitBoundsAB(Map map, Location a, Location b, double padFactor = 1.8)
    {
        double minLat = Math.Min(a.Latitude, b.Latitude);
        double maxLat = Math.Max(a.Latitude, b.Latitude);
        double minLng = Math.Min(a.Longitude, b.Longitude);
        double maxLng = Math.Max(a.Longitude, b.Longitude);

        var center = new Location((minLat + maxLat) / 2.0, (minLng + maxLng) / 2.0);
        var latDelta = Math.Max(0.005, (maxLat - minLat) * padFactor);
        var lngDelta = Math.Max(0.005, (maxLng - minLng) * padFactor);

        map.MoveToRegion(new MapSpan(center, latDelta, lngDelta));
    }


    // Encadra el mapa a la ruta
    static void FitMapTo(IEnumerable<(double lat, double lng)> pts, Map map)
    {
        var list = pts.ToList();
        if (list.Count == 0) return;

        double minLat = list.Min(p => p.lat), maxLat = list.Max(p => p.lat);
        double minLng = list.Min(p => p.lng), maxLng = list.Max(p => p.lng);
        var center = new Location((minLat + maxLat) / 2.0, (minLng + maxLng) / 2.0);

        // relleno para que no quede justo al borde
        var latDelta = Math.Max(0.005, (maxLat - minLat) * 1.8);
        var lngDelta = Math.Max(0.005, (maxLng - minLng) * 1.8);

        map.MoveToRegion(new MapSpan(center, latDelta, lngDelta));
    }


   

    void StartCountdown(Label target, BottomSheet sheet, ApiService.OfferItemDto o)
    {
        StopCountdown();

        _countdownTimer = Application.Current!.Dispatcher.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.IsRepeating = true;
        _countdownTimer.Tick += async (_, __) =>
        {
            if (!UpdateCountdownLabel(target))
            {
                // expiró
                StopBell();
                StopCountdown();
                await sheet.DismissAsync();         // cerrar popup
                await RefreshAndShowDirectAsync();  // refrescar lista
            }
        };
        _countdownTimer.Start();
    }

    bool UpdateCountdownLabel(Label target)
    {
        if (_expiresAtCurrent is null)
        {
            target.Text = "—";
            return true;
        }
        var now = DateTimeOffset.UtcNow;
        var end = _expiresAtCurrent.Value.ToUniversalTime();
        var left = end - now;
        if (left <= TimeSpan.Zero) { target.Text = "00:00"; return false; }
        if (left > TimeSpan.FromMinutes(5)) left = TimeSpan.FromMinutes(5); // cap por si reloj desfasado
        target.Text = $"{left.Minutes:00}:{left.Seconds:00}";
        return true;
    }

    void StopCountdown()
    {
        if (_countdownTimer is null) return;
        _countdownTimer.Stop();
        _countdownTimer.Tick -= null;
        _countdownTimer = null;
    }



    // Tap en ítem de ola
    private async void OnOfferTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ApiService.OfferItemDto offer) return;

        // Directas: deja que el flujo de directas lo maneje
        if (offer.is_direct == 1)
        {
            if (_queuedDirectIds.Add(offer.offer_id)) _directQueue.Enqueue(offer);
            if (!_directOpen) await ShowNextDirectAsync();
            return;
        }

        // ---- GUARD: si ya hay un sheet abierto para esta oferta, ignore ----
        if (_waveOpen && _waveOpenOfferId == offer.offer_id) return;
        if (_waveOpen) return; // otro sheet de ola ya visible

        _waveOpen = true;
        _waveOpenOfferId = offer.offer_id;

        var sheet = await BuildWaveOfferSheetAsync(offer);
        sheet.Dismissed += (_, __) =>
        {
            _waveOpen = false;
            _waveOpenOfferId = null;
        };

        await MainThread.InvokeOnMainThreadAsync(async () => await sheet.ShowAsync());
    }


 

    // --- Estado (botones superiores) ---
    private async void OnAvailableClicked(object? sender, EventArgs e)
    {
        var (lat, lng) = await GetCurrentLL();
        if (await _api.SendLocationAsync(lat, lng, busy: false))
        {
            _vm.IsAvailable = true; _vm.Availability = "Disponible";
            StartPolling();
            await RefreshAndShowDirectAsync();
        }
    }

    private async void OnBusyClicked(object? sender, EventArgs e)
    {
        var (lat, lng) = await GetCurrentLL();
        if (await _api.SendLocationAsync(lat, lng, busy: true))
        {
            _vm.IsAvailable = false; _vm.Availability = "Busy";
            StopPolling();
        }
    }

    // --- Acciones desde la lista (ola) ---
    private async void OnAcceptClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is ApiService.OfferItemDto offer)
        {
            // limpiar posibles duplicados en cola
            _queuedDirectIds.Remove(offer.offer_id);
            _directQueue.Clear();

            if (await _api.AcceptOfferAsync(offer.offer_id))
            {
                await Task.Delay(250);
                await RefreshAndShowDirectAsync();
                await Shell.Current.Navigation.PushAsync(new RidePage(offer));
            }
        }
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is ApiService.OfferItemDto offer)
        {
            _queuedDirectIds.Remove(offer.offer_id);
            _directQueue.Clear();

            if (await _api.RejectOfferAsync(offer.offer_id))
            {
                await Task.Delay(150);
                await RefreshAndShowDirectAsync();
            }
        }
    }

    // --- ubicación ---
    static async Task<(double lat, double lng)> GetCurrentLL()
    {
        try
        {
            var last = await Geolocation.GetLastKnownLocationAsync();
            if (last is not null) return (last.Latitude, last.Longitude);

            var req = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
            var loc = await Geolocation.GetLocationAsync(req);
            if (loc is not null) return (loc.Latitude, loc.Longitude);
        }
        catch { }
        return (0, 0);
    }

    // pull-to-refresh
    private async void OnRefresh(object sender, EventArgs e)
    {
        await SyncAvailabilityAsync();
        await RefreshAndShowDirectAsync();
        (sender as RefreshView)!.IsRefreshing = false;
    }
}
