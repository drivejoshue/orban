#if ANDROID
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.Graphics;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps.Handlers;
using Color = Android.Graphics.Color;
using ControlsMap = Microsoft.Maui.Controls.Maps.Map;
using PolylineForms = Microsoft.Maui.Controls.Maps.Polyline;

namespace OrbanaDrive.Platforms.Android;

public class MapReadyCallback : Java.Lang.Object, IOnMapReadyCallback
{
    private readonly ControlsMap _formsMap;
    private readonly IMapHandler _handler;

    public MapReadyCallback(ControlsMap formsMap, IMapHandler handler)
    {
        _formsMap = formsMap;
        _handler = handler;
    }

    public void OnMapReady(GoogleMap gmap)
    {
        var ctx = _handler.MauiContext?.Context;
        if (ctx is null) return;

        // UI limpia
        gmap.UiSettings.MapToolbarEnabled = false;
        gmap.UiSettings.CompassEnabled = false;
        gmap.UiSettings.ZoomControlsEnabled = false;
        gmap.UiSettings.RotateGesturesEnabled = true;
        gmap.UiSettings.TiltGesturesEnabled = false;

        // Tooltip (dist/ETA si la calculamos más abajo)
        gmap.SetInfoWindowAdapter(new RideInfoWindowAdapter(ctx));

        gmap.Clear();

        // ====== RUTA (polyline estilizada) ======
        double routeMeters = 0;
        foreach (var pl in _formsMap.MapElements.OfType<PolylineForms>())
        {
            var popts = new PolylineOptions()
                .InvokeWidth((float)Math.Max(4, pl.StrokeWidth))
                .Geodesic(true)
                .InvokeZIndex(0f)
                .InvokeColor(Color.Rgb(66, 133, 244))
                .InvokeStartCap(new RoundCap())
                .InvokeEndCap(new RoundCap())
                .InvokeJointType((int)JointType.Round);

            // patrón sutil (comenta si no lo quieres)
            var pattern = new List<PatternItem> { new Dash(30), new Gap(20) };
            popts.InvokePattern(pattern);

            LatLng? prev = null;

            foreach (var p in pl.Geopath)
            {
                var ll = new LatLng(p.Latitude, p.Longitude);
                popts.Add(ll);

                if (prev is not null)
                    routeMeters += Haversine(prev, ll); // sin .Value

                prev = ll;
            }

            gmap.AddPolyline(popts);
        }

        // ====== MARCADORES (PNG; sin pin rojo) ======
        // Heurística por etiquetas:
        bool hasYou = _formsMap.Pins.Any(p => string.Equals(p.Label?.Trim(), "Tú", StringComparison.OrdinalIgnoreCase));
        bool onlyAB = !hasYou &&
                      _formsMap.Pins.All(p => string.Equals(p.Label?.Trim(), "A", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(p.Label?.Trim(), "B", StringComparison.OrdinalIgnoreCase));

        // Dist/ETA para mostrar en B si aplica
        int? etaMin = routeMeters > 0 ? Math.Max(1, (int)Math.Round((routeMeters / 1000.0) / 24.0 * 60.0)) : null;
        var distStr = routeMeters > 0 ? $"{routeMeters / 1000.0:0.0} km" : null;
        var etaStr = etaMin.HasValue ? $"{etaMin} min" : null;
        var snippet = (distStr, etaStr) switch
        {
            (not null, not null) => $"{distStr} · {etaStr}",
            (not null, null) => distStr,
            (null, not null) => etaStr,
            _ => string.Empty
        };

        foreach (var pin in _formsMap.Pins)
        {
            var label = (pin.Label ?? string.Empty).Trim();
            var title = string.IsNullOrWhiteSpace(label) ? "Punto" : label;

            // Selección de icono según contexto:
            string iconName;
            if (hasYou)
            {
                // Contexto RidePage PICKUP: "Tú" + "A"
                if (label.Equals("Tú", StringComparison.OrdinalIgnoreCase))
                    iconName = "car_pin";            // tu coche
                else if (label.Equals("A", StringComparison.OrdinalIgnoreCase))
                    iconName = "passenger_pin";      // pasajero en pickup
                else
                    iconName = "car_pin";            // fallback
            }
            else if (onlyAB)
            {
                // Contexto RidePage ONBOARD: "A" -> "B" (ambos car_pin)
                iconName = "car_pin";
            }
            else
            {
                // Contexto OffersPage: A/B normales
                iconName = label.Equals("A", StringComparison.OrdinalIgnoreCase) ? "car_pin" : "car_pin";
            }

            var sub = (label.Equals("B", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(snippet))
                ? $"{pin.Address}\n{snippet}"
                : (pin.Address ?? "");

            var mopts = new MarkerOptions()
                .SetPosition(new LatLng(pin.Location.Latitude, pin.Location.Longitude))
                .SetTitle(title)
                .SetSnippet(sub)
                .InvokeZIndex(label == "A" ? 10f : 11f);

            var icon = MapPinIconHelper.FromBundle(ctx!, iconName);
            mopts.SetIcon(icon);

            var marker = gmap.AddMarker(mopts);

            // Abre tooltip de B por defecto cuando tenemos dist/ETA
            if (label.Equals("B", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(snippet))
                marker.ShowInfoWindow();
        }
    }

    // Distancia en metros (haversine)
    private static double Haversine(LatLng a, LatLng b)
    {
        const double R = 6371000.0;
        double dLat = ToRad(b.Latitude - a.Latitude);
        double dLon = ToRad(b.Longitude - a.Longitude);
        double lat1 = ToRad(a.Latitude), lat2 = ToRad(b.Latitude);
        double sin1 = Math.Sin(dLat / 2), sin2 = Math.Sin(dLon / 2);
        double h = sin1 * sin1 + Math.Cos(lat1) * Math.Cos(lat2) * sin2 * sin2;
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
#endif
