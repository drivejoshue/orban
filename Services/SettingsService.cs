// Services/SettingsService.cs
using Microsoft.Maui.Storage;

namespace OrbanaDrive.Services;

public class SettingsService
{
    const string KeyToken = "auth.token";
    const string KeyTenant = "auth.tenant";
    const string KeyBaseUrl = "api.baseurl";

    // ===== Base URL =====
    public string BaseUrl
    {
        get => Preferences.Get(KeyBaseUrl, "https://fe6ee20ecb03.ngrok-free.app"); // pon tu default
        set => Preferences.Set(KeyBaseUrl, value);
    }

    // ===== Token =====
    public Task<string?> GetTokenAsync()
        => Task.FromResult(Preferences.Get(KeyToken, (string?)null));

    public Task SetTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            Preferences.Remove(KeyToken);
        else
            Preferences.Set(KeyToken, token);
        return Task.CompletedTask;
    }

    // ===== Tenant =====
    public Task<int> GetTenantAsync()
        => Task.FromResult(Preferences.Get(KeyTenant, 1)); // default tenant 1

    public Task SetTenantAsync(int tenantId)
    {
        Preferences.Set(KeyTenant, tenantId);
        return Task.CompletedTask;
    }
    public Task SetRememberMeAsync(bool v) { Preferences.Set("auth.remember", v); return Task.CompletedTask; }
    public Task<bool> GetRememberMeAsync() => Task.FromResult(Preferences.Get("auth.remember", true));


    // Helpers opcionales
    public void ClearAll()
    {
        Preferences.Remove(KeyToken);
        Preferences.Remove(KeyTenant);
        // Preferences.Remove(KeyBaseUrl); // si quieres conservarlo, comenta esta línea
    }
}
