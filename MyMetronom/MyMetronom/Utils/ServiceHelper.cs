using Microsoft.Extensions.DependencyInjection;

namespace MyMetronom.Utils;

public static class ServiceHelper
{
    public static IServiceProvider Services { get; set; } = null!;

    public static T GetRequiredService<T>() where T : notnull
        => Services.GetRequiredService<T>();
}
