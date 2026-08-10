using Microsoft.Extensions.DependencyInjection;

namespace Markd;

public static class ServiceHelper
{
    public static T GetRequiredService<T>() where T : notnull
    {
        var services = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("Application services are not available.");

        return services.GetRequiredService<T>();
    }
}
