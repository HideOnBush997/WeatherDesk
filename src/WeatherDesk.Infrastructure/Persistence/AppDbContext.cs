using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WeatherDesk.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTimeOffset, string> UtcTimestampConverter = new(
        value => value.ToUniversalTime().ToString("O"),
        value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public DbSet<FavoriteCityEntity> FavoriteCities => Set<FavoriteCityEntity>();

    public DbSet<RecentCityEntity> RecentCities => Set<RecentCityEntity>();

    public DbSet<WeatherCacheEntity> WeatherCaches => Set<WeatherCacheEntity>();

    public DbSet<AppStateEntity> AppStates => Set<AppStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureFavoriteCities(modelBuilder.Entity<FavoriteCityEntity>());
        ConfigureRecentCities(modelBuilder.Entity<RecentCityEntity>());
        ConfigureWeatherCaches(modelBuilder.Entity<WeatherCacheEntity>());
        ConfigureAppStates(modelBuilder.Entity<AppStateEntity>());
    }

    private static void ConfigureFavoriteCities(EntityTypeBuilder<FavoriteCityEntity> entity)
    {
        entity.ToTable("FavoriteCities");
        entity.HasKey(city => city.Id);
        entity.Property(city => city.Id).ValueGeneratedOnAdd();
        entity.Property(city => city.LocationId).IsRequired();
        entity.Property(city => city.Name).IsRequired();
        entity.Property(city => city.Latitude).HasColumnType("REAL");
        entity.Property(city => city.Longitude).HasColumnType("REAL");
        entity.Property(city => city.AddedAtUtc)
            .HasConversion(UtcTimestampConverter)
            .HasColumnType("TEXT");
        entity.HasIndex(city => city.LocationId).IsUnique();
    }

    private static void ConfigureRecentCities(EntityTypeBuilder<RecentCityEntity> entity)
    {
        entity.ToTable("RecentCities");
        entity.HasKey(city => city.Id);
        entity.Property(city => city.Id).ValueGeneratedOnAdd();
        entity.Property(city => city.LocationId).IsRequired();
        entity.Property(city => city.Name).IsRequired();
        entity.Property(city => city.Latitude).HasColumnType("REAL");
        entity.Property(city => city.Longitude).HasColumnType("REAL");
        entity.Property(city => city.VisitedAtUtc)
            .HasConversion(UtcTimestampConverter)
            .HasColumnType("TEXT");
        entity.HasIndex(city => city.LocationId).IsUnique();
        entity.HasIndex(city => city.VisitedAtUtc);
    }

    private static void ConfigureWeatherCaches(EntityTypeBuilder<WeatherCacheEntity> entity)
    {
        entity.ToTable("WeatherCaches");
        entity.HasKey(cache => cache.LocationId);
        entity.Property(cache => cache.LocationId).IsRequired();
        entity.Property(cache => cache.PayloadJson).IsRequired().HasColumnType("TEXT");
        entity.Property(cache => cache.DataVersion).IsRequired();
        entity.Property(cache => cache.RetrievedAtUtc)
            .HasConversion(UtcTimestampConverter)
            .HasColumnType("TEXT");
        entity.Property(cache => cache.DataUpdatedAtUtc)
            .HasConversion(UtcTimestampConverter)
            .HasColumnType("TEXT");
        entity.Property(cache => cache.LocationNameSnapshot).IsRequired();
    }

    private static void ConfigureAppStates(EntityTypeBuilder<AppStateEntity> entity)
    {
        entity.ToTable("AppStates");
        entity.HasKey(state => state.Key);
        entity.Property(state => state.Key).IsRequired();
        entity.Property(state => state.Value).IsRequired().HasColumnType("TEXT");
    }
}
