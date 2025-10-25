using System;
using Microsoft.Extensions.DependencyInjection;

namespace OrbanaDrive.Helpers;

public static class ServiceHelper
{
    internal static IServiceProvider? _services;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("ServiceProvider no inicializado.");

    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();
}
