using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Services;
using WeatherDesk.Infrastructure.Configuration;
using WeatherDesk.Infrastructure.Ai;
using WeatherDesk.Infrastructure.Http;
using WeatherDesk.Infrastructure.Persistence;
using WeatherDesk.Infrastructure.Agent;
using WeatherDesk.Infrastructure.Time;
using WeatherDesk.App.ViewModels;

namespace WeatherDesk.App;

public static class ServiceRegistration
{
    public const string OpenMeteoClientName = "OpenMeteo";
    public const string DeepSeekClientName = "DeepSeek";

    public static IServiceCollection AddWeatherDeskServices(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddHttpClient<OpenMeteoGeocodingClient>(OpenMeteoClientName, client =>
            client.Timeout = OpenMeteoDefaults.RequestTimeout);
        services.AddTransient<IGeocodingService>(provider =>
            provider.GetRequiredService<OpenMeteoGeocodingClient>());
        services.AddHttpClient<OpenMeteoWeatherClient>(OpenMeteoClientName, client =>
            client.Timeout = OpenMeteoDefaults.RequestTimeout);
        services.AddTransient<IWeatherService>(provider =>
            provider.GetRequiredService<OpenMeteoWeatherClient>());
        services.AddTransient<WeatherQueryService>();
        services.TryAddSingleton<ISettingsStore, JsonSettingsStore>();
        services.TryAddSingleton<AppDbContextFactory>();
        services.TryAddSingleton<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>(
            provider => provider.GetRequiredService<AppDbContextFactory>());
        services.TryAddSingleton<ICacheStorage, EfCacheStorage>();
        services.TryAddSingleton<EfWeatherCache>(provider => EfWeatherCache.FromDbContextFactory(
            provider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EfWeatherCache>>()));
        services.TryAddSingleton<IWeatherCache>(provider =>
            new ResilientWeatherCache(provider.GetRequiredService<EfWeatherCache>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientWeatherCache>>()));
        services.TryAddSingleton<EfCityStore>();
        services.TryAddSingleton<ICityStore>(provider =>
            new ResilientCityStore(provider.GetRequiredService<EfCityStore>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientCityStore>>()));
        services.TryAddSingleton<EfAppStateStore>();
        services.TryAddSingleton<IAppStateStore>(provider =>
            new ResilientAppStateStore(provider.GetRequiredService<EfAppStateStore>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientAppStateStore>>()));
        services.TryAddSingleton<IWeatherTool, WeatherTool>();
        services.TryAddSingleton<IWeatherAgent, AgentService>();
        services.AddTransient<WeatherViewModel>();
        services.AddTransient<CitySearchViewModel>();
        services.AddTransient<CityListViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddHttpClient<DeepSeekClient>(DeepSeekClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.TryAddSingleton<DemoAnswerGenerator>();
        services.AddTransient<AnswerGeneratorSelector>();
        services.AddTransient<IAnswerGenerator>(provider =>
            provider.GetRequiredService<AnswerGeneratorSelector>());
        services.AddHttpClient(DeepSeekClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(30));

        // Feature modules add Domain-to-Infrastructure mappings, the
        // IDbContextFactory, and transient ViewModels as they are implemented.
        return services;
    }
}
