using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps.Handlers;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace OrbanaDrive.Helpers;

public static class MapNativeHelpers
{
    // Espera a que el Map tenga handler (evita NRE y “África”)
    public static async Task EnsureMapReadyFor(Map map)
    {
        int tries = 0;
        while (map?.Handler is null && tries++ < 90)
            await Task.Delay(16); // ~1.5s máximo
    }

    // Fuerza un redibujado nativo con nuestro MapReadyCallback
    public static void RedrawNativePins(Map map)
    {
#if ANDROID
        try
        {
            var handler = map?.Handler as IMapHandler;
            var mapView = handler?.PlatformView as Android.Gms.Maps.MapView;
            if (mapView is not null && handler is not null)
                mapView.GetMapAsync(new OrbanaDrive.Platforms.Android.MapReadyCallback(map, handler));
        }
        catch { /* no-op */ }
#endif
    }
}
