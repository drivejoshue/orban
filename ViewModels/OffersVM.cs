using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Dispatching;
using OrbanaDrive.Services;
using The49.Maui.BottomSheet;

namespace OrbanaDrive.ViewModels
{
    public partial class OffersVM : ObservableObject
    {
        private readonly ApiService _api;
        private readonly SessionService _session;

        public ObservableCollection<ApiService.OfferItemDto> Offers { get; } = new();

        [ObservableProperty] private bool isAvailable;              // true = Disponible
        [ObservableProperty] private string availability = "Busy";  // “Disponible” | “Busy”

        private IDispatcherTimer? _pollTimer;
        private readonly Queue<ApiService.OfferItemDto> _directQueue = new();
        private bool _directModalOpen;

        public OffersVM(ApiService api, SessionService session)
        {
            _api = api;
            _session = session;
        }



        // Llamada desde OffersPage.OnAppearing()
        [RelayCommand]
        public async Task InitAsync()
        {
            var me = await _api.GetSessionAsync();
            _session.Set(me);

            var status = me?.driver?.status?.ToLowerInvariant();
            IsAvailable = status == "idle" || status == "available";
            Availability = IsAvailable ? "Disponible" : "Busy";

            if (IsAvailable) StartPolling(); else StopPolling();
            await RefreshOffersAsync();
        }

        private void StartPolling()
        {
            _pollTimer ??= Application.Current!.Dispatcher.CreateTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(5);
            _pollTimer.IsRepeating = true;
            _pollTimer.Tick -= PollTick;
            _pollTimer.Tick += PollTick;
            _pollTimer.Start();
        }

        private void StopPolling()
        {
            if (_pollTimer is null) return;
            _pollTimer.Stop();
            _pollTimer.Tick -= PollTick;
        }

        private async void PollTick(object? sender, EventArgs e) => await RefreshOffersAsync();

        // ---- Ofertas ----
        [RelayCommand]
        
        public async Task RefreshOffersAsync()
        {
            var list = await _api.GetOffersAsync("offered");

            // Fallback por si backend aún no trae is_direct en alguna
            foreach (var it in list)
                if (it.is_direct != 0 && it.is_direct != 1)
                    it.is_direct = it.round_no == 0 ? 1 : 0;

            var direct = list.Where(o => o.is_direct == 1).ToList();
            var wave = list.Where(o => o.is_direct == 0).ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Offers.Clear();
                foreach (var o in wave) Offers.Add(o);
            });

            foreach (var d in direct) _directQueue.Enqueue(d);

            if (!_directModalOpen && _directQueue.Count > 0)
                _ = ShowDirectModalAsync(_directQueue.Dequeue());
        }


        // ---- Disponibilidad ----
        [RelayCommand]
        public async Task GoAvailableAsync()
        {
            var (lat, lng) = await GetCurrentLL();
            if (await _api.SetBusyAsync(false, lat, lng))
            {
                IsAvailable = true; Availability = "Disponible";
                StartPolling();
                await RefreshOffersAsync();
            }
        }

        [RelayCommand]
        public async Task GoBusyAsync()
        {
            var (lat, lng) = await GetCurrentLL();
            if (await _api.SetBusyAsync(true, lat, lng))
            {
                IsAvailable = false; Availability = "Busy";
                StopPolling();
                // Opcional: Offers.Clear();
            }
        }

        // ---- Aceptar/Rechazar desde la lista (ola) ----
        [RelayCommand]
        public async Task AcceptOfferAsync(long offerId)
        {
            if (await _api.AcceptOfferAsync(offerId))
            {
                await Shell.Current.Navigation.PushAsync(Helpers.ServiceHelper.Get<Views.RidePage>());
            }
            await RefreshOffersAsync();
        }

        [RelayCommand]
        public async Task RejectOfferAsync(long offerId)
        {
            if (await _api.RejectOfferAsync(offerId))
                await RefreshOffersAsync();
        }

        // ---- Modal para ofertas directas ----
        private async Task ShowDirectModalAsync(ApiService.OfferItemDto offer)
        {
            _directModalOpen = true;

            var sheet = BuildDirectOfferSheet(offer);
            sheet.Dismissed += async (_, __) =>
            {
                _directModalOpen = false;
                if (_directQueue.Count > 0)
                    await ShowDirectModalAsync(_directQueue.Dequeue());
            };

            // Envolver en lambda (Action) para .NET 8
            await MainThread.InvokeOnMainThreadAsync(async () => await sheet.ShowAsync());
        }

        private BottomSheet BuildDirectOfferSheet(ApiService.OfferItemDto o)
        {
            var sheet = new BottomSheet { HasBackdrop = true, IsCancelable = false };
            sheet.Detents.Clear();
            sheet.Detents.Add(new MediumDetent()); // LargeDetent no existe en tu paquete

            var title = new Label { Text = "Nuevo servicio", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
            var origin = new Label { Text = o.origin_label, FontSize = 16, TextColor = Colors.White };
            var dest = new Label { Text = o.dest_label, FontSize = 14, TextColor = Colors.LightGray };

            Border Chip(string text) => new Border
            {
                Stroke = Colors.Gray,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(8, 4),
                Background = new SolidColorBrush(Color.FromArgb("#232323")),
                Content = new Label { Text = text, FontSize = 12, TextColor = Colors.LightGray }
            };

            var chips = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    Chip($"ETA: {o.eta_seconds ?? 0}s"),
                    Chip($"Dist: {o.distance_m ?? o.ride_distance_m ?? 0} m"),
                    Chip($"${o.quoted_amount ?? 0}")
                }
            };

            var btnAccept = new Button
            {
                Text = "ACEPTAR",
                BackgroundColor = Color.FromArgb("#2F6FED"),
                TextColor = Colors.White,
                CornerRadius = 12,
                Padding = new Thickness(14, 10)
            };

            var btnReject = new Button
            {
                Text = "RECHAZAR",
                BackgroundColor = Color.FromArgb("#D35454"),
                TextColor = Colors.White,
                CornerRadius = 12,
                Padding = new Thickness(14, 10)
            };

            btnAccept.Clicked += async (_, __) =>
            {
                if (await _api.AcceptOfferAsync(o.offer_id))
                {
                    await sheet.DismissAsync();
                    await Shell.Current.Navigation.PushAsync(Helpers.ServiceHelper.Get<Views.RidePage>());
                }
                else
                {
                    await sheet.DismissAsync();
                }
            };

            btnReject.Clicked += async (_, __) =>
            {
                await _api.RejectOfferAsync(o.offer_id);
                await sheet.DismissAsync();
            };

            var actions = new Grid
            {
                Margin = new Thickness(0, 10, 0, 0),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(), new ColumnDefinition()
                }
            };
            actions.Children.Add(btnReject);
            Grid.SetColumn(btnReject, 0);
            actions.Children.Add(btnAccept);
            Grid.SetColumn(btnAccept, 1);

            sheet.Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 10,
                Children = { title, origin, dest, chips, actions }
            };

            return sheet;
        }

        private static async Task<(double lat, double lng)> GetCurrentLL()
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
    }
}
