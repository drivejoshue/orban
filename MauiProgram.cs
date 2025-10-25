// MauiProgram.cs
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using The49.Maui.BottomSheet;
using OrbanaDrive.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.LifecycleEvents;
using OrbanaDrive.Helpers;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Plugin.Maui.Audio;

// ALIAS útil
using ControlsMap = Microsoft.Maui.Controls.Maps.Map;

#if ANDROID
using Microsoft.Maui.Maps.Handlers;          // MapHandler
using Android.Gms.Maps;                      // MapView
using OrbanaDrive.Platforms.Android;         // MapReadyCallback
#endif

namespace OrbanaDrive;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
            .UseMauiCommunityToolkit()
            .UseBottomSheet()
            .ConfigureFonts(fonts => { /* fonts */ });

#if ANDROID
       builder.ConfigureMauiHandlers(handlers =>
{
    // Handler estándar del Map
    handlers.AddHandler(typeof(ControlsMap), typeof(Microsoft.Maui.Maps.Handlers.MapHandler));

    // 1) Desactivar el render nativo de Pins (evita el pin rojo)
    MapHandler.Mapper.ModifyMapping(nameof(ControlsMap.Pins), (handler, view, action) =>
    {
        // no llamamos "action" => NO se dibujan los pins nativos
    });

     MapHandler.Mapper.ModifyMapping(nameof(ControlsMap.MapElements), (handler, view, action) =>
    {
        // no-op
    });

    // 2) Conectar nuestro callback para dibujar PNG + polyline + tooltip
    MapHandler.Mapper.AppendToMapping("HookGoogleMap", (handler, iMap) =>
    {
        var mapView = handler.PlatformView as MapView;
        if (mapView != null && iMap is ControlsMap formsMap)
        {
            mapView.GetMapAsync(new MapReadyCallback(formsMap, handler));
        }
    });
});
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ===== Servicios (igual que ya tienes) =====
        builder.Services.AddSingleton<IAudioManager>(_ => AudioManager.Current);
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddSingleton<MapsService>();

        builder.Services.AddSingleton<ApiService>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();
            var http = new HttpClient
            {
                BaseAddress = new Uri(settings.BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            return new ApiService(http, settings);
        });

        // ===== VMs + Pages ===== (igual)
        builder.Services.AddTransient<ViewModels.LoginVM>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddSingleton<ViewModels.HomeVM>();
        builder.Services.AddTransient<Views.HomePage>();

        builder.Services.AddTransient<ViewModels.OffersVM>();
        builder.Services.AddTransient<ViewModels.RideVM>();
        builder.Services.AddTransient<ViewModels.StandsVM>();
        builder.Services.AddTransient<ViewModels.SettingsVM>();

        builder.Services.AddTransient<Views.OffersPage>();
        builder.Services.AddTransient<Views.RidePage>();
        builder.Services.AddTransient<Views.StandsPage>();
        builder.Services.AddTransient<Views.SettingsPage>();

        // ===== Lifecycle (igual) =====
        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            events.AddAndroid(android =>
            {
                android.OnResume(activity =>
                {
                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        var shell = Application.Current?.MainPage as Shell;
                        if (shell?.CurrentPage is Views.HomePage hp &&
                            hp.BindingContext is ViewModels.HomeVM hvm)
                        {
                            await hvm.InitAsync();
                        }
                        else if (shell?.CurrentPage is NavigationPage nav &&
                                 nav.CurrentPage is Views.HomePage hp2 &&
                                 hp2.BindingContext is ViewModels.HomeVM hvm2)
                        {
                            await hvm2.InitAsync();
                        }
                    });
                });
            });
#endif
        });

        var app = builder.Build();
        ServiceHelper._services = app.Services;
        return app;
    }
}
