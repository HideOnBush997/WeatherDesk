using Microsoft.Extensions.DependencyInjection;
using WeatherDesk.App;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Infrastructure.Http;
using WeatherDesk.Infrastructure.Configuration;
using WeatherDesk.Infrastructure.Persistence;
using WeatherDesk.Infrastructure.Ai;

namespace WeatherDesk.Tests.AppTests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void Registers_the_geocoding_domain_contract()
    {
        var services = new ServiceCollection();
        services.AddWeatherDeskServices();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<OpenMeteoGeocodingClient>(provider.GetRequiredService<IGeocodingService>());
    }

    [Fact]
    public void Registers_the_weather_domain_contract()
    {
        var services = new ServiceCollection();
        services.AddWeatherDeskServices();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<OpenMeteoWeatherClient>(provider.GetRequiredService<IWeatherService>());
    }

    [Fact]
    public void Registers_settings_and_database_factories()
    {
        var services = new ServiceCollection();
        services.AddWeatherDeskServices();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<JsonSettingsStore>(provider.GetRequiredService<ISettingsStore>());
        Assert.IsType<AppDbContextFactory>(provider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>());
    }

    [Fact]
    public void Registers_answer_generator_selector()
    {
        var services = new ServiceCollection();
        services.AddWeatherDeskServices();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<AnswerGeneratorSelector>(provider.GetRequiredService<IAnswerGenerator>());
    }
}
