using Microsoft.Extensions.DependencyInjection;

namespace SecureMesh.App.Services;

/// <summary>
/// Minimal service locator used by code-behind pages. Pages created from XAML
/// <c>DataTemplate</c> do not go through constructor injection, so they resolve
/// their dependencies here from the app-wide service provider.
/// </summary>
public static class ServiceHelper
{
    public static T Get<T>() where T : notnull
    {
        var services = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("The application service provider is not available yet.");
        return services.GetRequiredService<T>();
    }
}
