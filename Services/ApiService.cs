using Microsoft.Maui.Networking;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbanaDrive.Services;

public class ApiService
{
    readonly HttpClient _http;
    readonly SettingsService _settings;
    static bool _httpDebug = true;
    // ApiService.cs
    public string BaseUrl => _settings.BaseUrl;

    static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString  // <— clave para "tenant_id":"1"
    };

    public ApiService(HttpClient http, SettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    // ---------- headers comunes ----------
    async Task AddCommonHeadersAsync()
    {
        _http.DefaultRequestHeaders.Remove("Accept");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        _http.DefaultRequestHeaders.Authorization = null;
        var token = await _settings.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Replace("Bearer ", ""));

        _http.DefaultRequestHeaders.Remove("X-Tenant-ID");
        var tenant = await _settings.GetTenantAsync();
        if (tenant > 0)
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Tenant-ID", tenant.ToString());

        // LOG
        Log($"BaseAddress={_http.BaseAddress}");
        Log($"Headers: Accept=application/json, Auth={(token != null ? "Bearer " + Mask(token) : "(none)")}, X-Tenant-ID={tenant}");
    }


    // ---------- modelos ----------
    class LoginDto
    {
        public string? token { get; set; }
        public MeDto.UserDto? user { get; set; }
    }

    public class LoginResult { public bool Ok; public string? Message; }

    public class MeDto
    {
        public bool ok { get; set; }
        public UserDto? user { get; set; }
        public DriverDto? driver { get; set; }
        public ShiftDto? current_shift { get; set; }
        public VehicleDto? vehicle { get; set; }

        public class UserDto
        {
            public int id { get; set; }
            public string? name { get; set; }
            public string? email { get; set; }
            public int tenant_id { get; set; } // con el fix del #2 ya acepta "1"
        }

        public class DriverDto
        {
            public int id { get; set; }
            public string? status { get; set; }

            // **Campos opcionales que tu backend ya envía**
            public string? name { get; set; }
            public string? phone { get; set; }
            public string? email { get; set; }

            // **El que faltaba:**
            public string? foto_path { get; set; }

            // ult. ubicación (si las usas)
            public string? last_lat { get; set; }
            public string? last_lng { get; set; }
            public string? last_ping_at { get; set; }
        }

        public class ShiftDto
        {
            public int id { get; set; }
            public string? status { get; set; }      // "abierto" | "cerrado"
            public string? started_at { get; set; }
            public string? ended_at { get; set; }    // <— necesario
            public int? vehicle_id { get; set; }
        }

        public class VehicleDto
        {
            public int id { get; set; }
            public string? economico { get; set; }
            public string? plate { get; set; }
            public string? brand { get; set; }
            public string? model { get; set; }
            public string? type { get; set; }
        }
    }


    public class StartShiftDto { public bool ok { get; set; } public int shift_id { get; set; } }
    public class OkDto { public bool ok { get; set; } public string? message { get; set; } }

    // payloads (sin 'record' para compatibilidad)
    class StartShiftPayload { public int? vehicle_id { get; set; } public StartShiftPayload(int? v) { vehicle_id = v; } }
    class FinishShiftPayload { public int? shift_id { get; set; } public FinishShiftPayload(int? s) { shift_id = s; } }
    class LocationPayload { public double lat; public double lng; public bool? busy; public LocationPayload(double a, double b, bool? c) { lat = a; lng = b; busy = c; } }

    // ApiService.Offers DTOs
    public sealed class OfferListDto
    {
        public bool ok { get; set; }
        public DriverRef? driver { get; set; }
        public int count { get; set; }
        public List<OfferItemDto> items { get; set; } = new();
        public sealed class DriverRef
        {
            public int id { get; set; }
            public int tenant_id { get; set; }
        }
    }

    public sealed class OfferItemDto
    {
        // ----- offer_* (de ride_offers)
        public long offer_id { get; set; }
        public string? offer_status { get; set; }          // offered/accepted/rejected/...
        public string? sent_at { get; set; }
        public string? responded_at { get; set; }
        public int is_direct { get; set; } // 1=directa, 0=ola
        public string? expires_at { get; set; } 
        public int? eta_seconds { get; set; }
        public int? distance_m { get; set; }
        public int round_no { get; set; }

        // ----- ride_* (de rides)
        public long ride_id { get; set; }
        public string? ride_status { get; set; }
        public string? passenger_name { get; set; }
        public string? passenger_phone { get; set; }
        public string? route_polyline { get; set; }

        public string? origin_label { get; set; }
        public double origin_lat { get; set; }
        public double origin_lng { get; set; }

        public string? dest_label { get; set; }
        public double? dest_lat { get; set; }
        public double? dest_lng { get; set; }

        public decimal? quoted_amount { get; set; }
        public int? ride_distance_m { get; set; }
        public int? ride_duration_s { get; set; }
    }

    // ---------- helpers ----------
    void Log(string msg)
    {
        if (_httpDebug) Debug.WriteLine($"[Api] {msg}");
    }

    // enmascara el token para no exponerlo completo en logs
    static string Mask(string? s, int keep = 4)
    {
        if (string.IsNullOrEmpty(s)) return "(null/empty)";
        var t = s.Replace("Bearer ", "");
        if (t.Length <= keep) return new string('*', t.Length);
        return new string('*', Math.Max(0, t.Length - keep)) + t.Substring(t.Length - keep);
    }

    public async Task<string> GetSessionRawAsync()
    {
        await AddCommonHeadersAsync();
        var res = await _http.GetAsync("/api/auth/me");
        var raw = await res.Content.ReadAsStringAsync();
        return $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}\n{raw}";
    }

    static async Task<T?> ReadAs<T>(HttpResponseMessage res)
    {
        var raw = await res.Content.ReadAsStringAsync();
        System.Diagnostics.Debug.WriteLine($"[HTTP {res.RequestMessage?.Method} {res.RequestMessage?.RequestUri}] {res.StatusCode} ~ {raw}");
        if (!res.IsSuccessStatusCode) return default;
        try { return JsonSerializer.Deserialize<T>(raw, _jsonOpts); }
        catch { return default; }
    }

    async Task<bool> HandleAuthFailure(HttpResponseMessage res)
    {
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await _settings.SetTokenAsync(null);
            return true;
        }
        return false;
    }

    // =========================================================
    //                      AUTH
    // =========================================================
    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/auth/login", new { email, password });
            var dto = await ReadAs<LoginDto>(res);
            if (!res.IsSuccessStatusCode || dto?.token is null)
                return new LoginResult { Ok = false, Message = "Credenciales inválidas" };

            await _settings.SetTokenAsync(dto.token);
            if (dto.user is not null)
                await _settings.SetTenantAsync(dto.user.tenant_id);

            return new LoginResult { Ok = true };
        }
        catch (Exception ex)
        {
            return new LoginResult { Ok = false, Message = ex.Message };
        }
    }

    public async Task<bool> MePingAsync()
    {
        await AddCommonHeadersAsync();
        try
        {
            var res = await _http.GetAsync("/api/auth/me");
            if (await HandleAuthFailure(res)) return false;
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<MeDto?> GetSessionAsync()
    {
        await AddCommonHeadersAsync();
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                Log("Sin Internet (Connectivity).");
                return null;
            }

            var url = "/api/auth/me";
            Log($"GET {url}");
            var res = await _http.GetAsync(url);

            Log($"← {((int)res.StatusCode)} {res.ReasonPhrase}");
            foreach (var h in res.Headers) Log($"  H: {h.Key}: {string.Join(", ", h.Value)}");
            foreach (var h in res.Content.Headers) Log($"  CH: {h.Key}: {string.Join(", ", h.Value)}");

            var raw = await res.Content.ReadAsStringAsync();
            Log($"RAW: {raw}");

            if (await HandleAuthFailure(res)) { Log("Auth fail → token limpiado"); return null; }
            if (!res.IsSuccessStatusCode) { Log("No OK → null"); return null; }

            try
            {
                var dto = JsonSerializer.Deserialize<MeDto>(raw, _jsonOpts);
                Log($"me.ok={dto?.ok} user.id={dto?.user?.id} driver.id={dto?.driver?.id} " +
                    $"shift.id={dto?.current_shift?.id} status={dto?.current_shift?.status} " +
                    $"ended_at={dto?.current_shift?.ended_at} veh={dto?.vehicle?.economico}/{dto?.vehicle?.plate}");
                return dto;
            }
            catch (Exception ex)
            {
                Log($"Deserialize error: {ex.Message}");
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            Log($"GetSessionAsync HttpReq error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Log($"GetSessionAsync error: {ex.Message}");
            return null;
        }
    }


    public async Task LogoutAsync()
    {
        try
        {
            await AddCommonHeadersAsync();
            await _http.PostAsync("/api/auth/logout", null);
        }
        catch { /* swallow */ }
        finally
        {
            await _settings.SetTokenAsync(null);
        }
    }

    // =========================================================
    //                     TURNOS DRIVER
    // =========================================================
    public async Task<(bool ok, int? shiftId, string? msg)> StartShiftAsync(int? vehicleId = null)
    {
        await AddCommonHeadersAsync();
        try
        {
            var res = await _http.PostAsJsonAsync("/api/driver/shifts/start", new StartShiftPayload(vehicleId));
            var dto = await ReadAs<StartShiftDto>(res);
            var ok = res.IsSuccessStatusCode && (dto?.ok ?? false);
            return (ok, dto?.shift_id, ok ? null : "No se pudo abrir turno");
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    // shiftId opcional (backend cierra el último abierto si no se manda)
    public async Task<(bool ok, string? msg)> FinishShiftAsync(int? shiftId = null)
    {
        await AddCommonHeadersAsync();
        try
        {
            var res = await _http.PostAsJsonAsync("/api/driver/shifts/finish", new FinishShiftPayload(shiftId));
            var dto = await ReadAs<OkDto>(res);
            return (res.IsSuccessStatusCode && (dto?.ok ?? false), dto?.message);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // =========================================================
    //                  UBICACIÓN / BUSY
    // =========================================================
    // Services/ApiService.Location.cs (o dentro de ApiService)
    public async Task<bool> SendLocationAsync(double lat, double lng, bool? busy = null, double? speedKmh = null)
    {
        await AddCommonHeadersAsync();

        var url = "/api/driver/location"; // tu ruta admite PUT o POST, usa POST
        var payload = new Dictionary<string, object?>
        {
            ["lat"] = lat,
            ["lng"] = lng
        };
        if (busy.HasValue)
            payload["busy"] = busy.Value;
        if (speedKmh.HasValue)
            payload["speed_kmh"] = speedKmh.Value;

        var json = JsonSerializer.Serialize(payload, _jsonOpts);
        Log($"POST {url} BODY: {json}");
        using var res = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        if (await HandleAuthFailure(res)) return false;
        if (!res.IsSuccessStatusCode) return false;

        var raw = await res.Content.ReadAsStringAsync();
        Log($"SendLocation RAW: {raw}");
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
        }
        catch { return true; }
    }
    public async Task<bool> SetBusyAsync(bool busy, double lat = 0, double lng = 0)
    {
        await AddCommonHeadersAsync();
        var bodyObj = new { lat = lat, lng = lng, busy = busy };
        var body = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync("/api/driver/location", body);
        if (await HandleAuthFailure(res)) return false;
        return res.IsSuccessStatusCode;
    }

    // ApiService.cs
    public async Task<string?> GetDriverStatusAsync()
    {
        await AddCommonHeadersAsync();
        using var res = await _http.GetAsync("/api/auth/me");
        if (!res.IsSuccessStatusCode) return null;

        var raw = await res.Content.ReadAsStringAsync();
        try
        {
            var dto = JsonSerializer.Deserialize<MeDto>(raw, _jsonOpts);
            return dto?.driver?.status?.ToLowerInvariant();
        }
        catch { return null; }
    }


    // =========================================================
    //             OFERTAS Y TRANSICIONES DE RIDES
    // =========================================================

    // GET /api/driver/offers?status=offered|accepted|...
    public async Task<List<OfferItemDto>> GetOffersAsync(string? status = null)
    {
        try
        {
            // evita llamadas si no hay internet
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return new List<OfferItemDto>();

            await AddCommonHeadersAsync();

            var url = "/api/driver/offers";
            if (!string.IsNullOrWhiteSpace(status))
                url += $"?status={Uri.EscapeDataString(status)}";

            Log($"GET {url}");
            using var res = await _http.GetAsync(url);

            if (await HandleAuthFailure(res)) return new List<OfferItemDto>();
            if (!res.IsSuccessStatusCode) return new List<OfferItemDto>();

            var raw = await res.Content.ReadAsStringAsync();
            var dto = JsonSerializer.Deserialize<OfferListDto>(raw, _jsonOpts);
            return dto?.items ?? new List<OfferItemDto>();
        }
        catch (HttpRequestException ex)
        {
            Log($"GetOffersAsync Http error: {ex.Message}");
            return new List<OfferItemDto>();
        }
        catch (Exception ex)
        {
            Log($"GetOffersAsync error: {ex.Message}");
            return new List<OfferItemDto>();
        }
    }


    // POST /api/driver/offers/{id}/accept
    public async Task<bool> AcceptOfferAsync(long offerId)
    {
        await AddCommonHeadersAsync();
        var url = $"/api/driver/offers/{offerId}/accept";
        Log($"POST {url}");
        using var res = await _http.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));
        if (await HandleAuthFailure(res)) return false;
        if (!res.IsSuccessStatusCode) return false;

        // opcional: validar {"ok":true}
        var raw = await res.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
        }
        catch { return true; }
    }

    // POST /api/driver/offers/{id}/reject
    public async Task<bool> RejectOfferAsync(long offerId)
    {
        await AddCommonHeadersAsync();
        var url = $"/api/driver/offers/{offerId}/reject";
        Log($"POST {url}");
        using var res = await _http.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));
        if (await HandleAuthFailure(res)) return false;
        if (!res.IsSuccessStatusCode) return false;

        var raw = await res.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
        }
        catch { return true; }
    }

    public async Task<OfferItemDto?> GetActiveRideAsync()
    {
        await AddCommonHeadersAsync();
        var res = await _http.GetAsync("/api/driver/rides/active");
        if (await HandleAuthFailure(res)) return null;
        if (!res.IsSuccessStatusCode) return null;

        var raw = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("item", out var el) || el.ValueKind == JsonValueKind.Null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<OfferItemDto>(el.GetRawText(), _jsonOpts);
        }
        catch { return null; }
    }

    public async Task<bool> RideArrivedAsync(int rideId)
    {
        await AddCommonHeadersAsync();
        var res = await _http.PostAsync($"/api/driver/rides/{rideId}/arrived", null);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RideBoardAsync(int rideId)
    {
        await AddCommonHeadersAsync();
        var res = await _http.PostAsync($"/api/driver/rides/{rideId}/board", null);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RideFinishAsync(int rideId)
    {
        await AddCommonHeadersAsync();
        var res = await _http.PostAsync($"/api/driver/rides/{rideId}/finish", null);
        return res.IsSuccessStatusCode;
    }

    public sealed class RoutePointDto { public double lat { get; set; } public double lng { get; set; } }
    public sealed class RouteResponseDto
    {
        public bool ok { get; set; }
        public List<RoutePointDto>? points { get; set; }  // asumiendo que tu API devuelve { ok, points:[{lat,lng},...] }
    }

    public sealed class RouteDto
    {
        public bool ok { get; set; }
        public string? polyline { get; set; }           // si el backend la manda
        public List<double[]?>? path { get; set; }      // o [[lat,lng],...]
    }

    public async Task<List<(double lat, double lng)>?> GetRouteAsync(
      double fromLat, double fromLng, double toLat, double toLng, string mode = "driving")
    {
        await AddCommonHeadersAsync();

        var payload = new
        {
            from = new { lat = fromLat, lng = fromLng },
            to = new { lat = toLat, lng = toLng },
            mode
        };

        var url = "/api/driver/geo/route";
        Log($"POST {url} route request");
        using var res = await _http.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        if (await HandleAuthFailure(res)) return null;
        var raw = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean()) return null;

        // 1) points (preferido si viene)
        if (root.TryGetProperty("points", out var pointsEl) && pointsEl.ValueKind == JsonValueKind.Array)
        {
            var list = new List<(double, double)>();
            foreach (var p in pointsEl.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
                    list.Add((p[0].GetDouble(), p[1].GetDouble()));
            }
            return list.Count > 0 ? list : null;
        }

        // 2) polyline
        if (root.TryGetProperty("polyline", out var polyEl) && polyEl.ValueKind == JsonValueKind.String)
        {
            var poly = polyEl.GetString();
            return !string.IsNullOrEmpty(poly) ? PolylineDecode(poly) : null;
        }

        return null;
    }

    // Decodificador de polyline (Google) – retorna lista lat/lng
    private static List<(double lat, double lng)> PolylineDecode(string polyline)
    {
        var poly = new List<(double, double)>();
        int index = 0, lat = 0, lng = 0;

        while (index < polyline.Length)
        {
            int b, shift = 0, result = 0;
            do { b = polyline[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
            lat += dlat;

            shift = 0; result = 0;
            do { b = polyline[index++] - 63; result |= (b & 0x1f) << shift; shift += 5; } while (b >= 0x20);
            int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
            lng += dlng;

            poly.Add((lat / 1E5, lng / 1E5));
        }
        return poly;
    }



    // Services/ApiService.cs
    // Services/ApiService.cs
    public async Task<List<string>> GetCancelReasonsAsync()
    {
        await AddCommonHeadersAsync();
        using var res = await _http.GetAsync("/api/driver/cancel-reasons");
        if (await HandleAuthFailure(res)) return new();
        if (!res.IsSuccessStatusCode) return new();

        var raw = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            return arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        return new();
    }

    public async Task<bool> DriverCancelRideAsync(int rideId, string? reason)
    {
        await AddCommonHeadersAsync();
        var payload = new { reason = reason };
        var json = new StringContent(JsonSerializer.Serialize(payload, _jsonOpts), Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync($"/api/driver/rides/{rideId}/cancel", json);
        if (await HandleAuthFailure(res)) return false;
        if (!res.IsSuccessStatusCode) return false;

        var raw = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    }


    //------settings  dispatch 



}
