// Services/MapsService.cs
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace OrbanaDrive.Services;

public class MapsService
{
    public static List<(double lat, double lng)> DecodePolyline(string poly)
    {
        var list = new List<(double, double)>();
        int index = 0, lat = 0, lng = 0;

        while (index < poly.Length)
        {
            int b, shift = 0, result = 0;
            do { b = poly[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
            lat += dlat;

            shift = 0; result = 0;
            do { b = poly[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
            lng += dlng;

            list.Add((lat / 1E5, lng / 1E5));
        }
        return list;
    }

    public void DrawRoute(Map map, IEnumerable<(double lat, double lng)> pts)
    {
        map.MapElements.Clear();
        var poly = new Polyline { StrokeWidth = 6 };
        foreach (var (la, ln) in pts) poly.Geopath.Add(new Location(la, ln));
        map.MapElements.Add(poly);
    }

    public void FitToPositions(Map map, IEnumerable<(double lat, double lng)> pts, double paddingKm = 0.3)
    {
        var list = pts.ToList();
        if (list.Count == 0) return;
        var minLat = list.Min(p => p.lat); var maxLat = list.Max(p => p.lat);
        var minLng = list.Min(p => p.lng); var maxLng = list.Max(p => p.lng);

        var center = new Location((minLat + maxLat) / 2.0, (minLng + maxLng) / 2.0);
        // zoom aproximado por bounding box
        map.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(
            Math.Max(paddingKm, HaversineKm(minLat, minLng, maxLat, maxLng) / 2.0 + paddingKm))));
    }

    static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        double R = 6371, dLat = ToRad(lat2 - lat1), dLon = ToRad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
    static double ToRad(double x) => x * Math.PI / 180.0;
}

public static class MapUtils
{
    public static void SetInitialRegion(Map map, Location center, double kmRadius = 1.2)
    {
        if (double.IsNaN(center.Latitude) || double.IsNaN(center.Longitude)) return;
        var span = MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(kmRadius));
        map.MoveToRegion(span); // <-- evita África
    }

    public static void FitTo(List<(double lat, double lng)> pts, Map map)
    {
        if (pts.Count == 0) return;
        double minLat = pts.Min(p => p.lat), maxLat = pts.Max(p => p.lat);
        double minLng = pts.Min(p => p.lng), maxLng = pts.Max(p => p.lng);
        var center = new Location((minLat + maxLat) / 2.0, (minLng + maxLng) / 2.0);

        var latDelta = Math.Max(0.005, (maxLat - minLat) * 1.8);
        var lngDelta = Math.Max(0.005, (maxLng - minLng) * 1.8);
        map.MoveToRegion(new MapSpan(center, latDelta, lngDelta));
    }
}
