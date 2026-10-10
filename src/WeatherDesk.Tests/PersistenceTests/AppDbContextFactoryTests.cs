using Microsoft.EntityFrameworkCore;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Persistence;

namespace WeatherDesk.Tests.PersistenceTests;

public sealed class AppDbContextFactoryTests
{
    [Fact]
    public async Task Initialize_creates_the_schema_indexes_and_five_second_command_timeout()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "data", "weatherdesk.db");
        var factory = new AppDbContextFactory(databasePath);

        var result = await factory.InitializeAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(databasePath));
        await using var context = await factory.CreateDbContextAsync();
        Assert.Equal(5, context.Database.GetCommandTimeout());

        var tables = await ReadStringsAsync(context,
            "SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.Contains("FavoriteCities", tables);
        Assert.Contains("RecentCities", tables);
        Assert.Contains("WeatherCaches", tables);
        Assert.Contains("AppStates", tables);

        var favoriteIndexes = await ReadIndexesAsync(context, "FavoriteCities");
        Assert.Contains(favoriteIndexes, index => index.IsUnique && index.Columns.SequenceEqual(["LocationId"]));

        var recentIndexes = await ReadIndexesAsync(context, "RecentCities");
        Assert.Contains(recentIndexes, index => index.IsUnique && index.Columns.SequenceEqual(["LocationId"]));
        Assert.Contains(recentIndexes, index => !index.IsUnique && index.Columns.SequenceEqual(["VisitedAtUtc"]));

        Assert.Equal("LocationId", await ReadPrimaryKeyColumnAsync(context, "WeatherCaches"));
        Assert.Equal("Key", await ReadPrimaryKeyColumnAsync(context, "AppStates"));
        Assert.Equal(1, await ReadUserVersionAsync(context));
    }

    [Fact]
    public async Task Initialize_is_idempotent_and_preserves_existing_data()
    {
        using var directory = new TemporaryDirectory();
        var factory = new AppDbContextFactory(Path.Combine(directory.Path, "weatherdesk.db"));
        Assert.True((await factory.InitializeAsync(CancellationToken.None)).IsSuccess);

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO AppStates (Key, Value) VALUES ({"LastCityId"}, {"tokyo"})");
        }

        var secondStart = await factory.InitializeAsync(CancellationToken.None);

        Assert.True(secondStart.IsSuccess);
        await using var reopenedContext = await factory.CreateDbContextAsync();
        Assert.Equal("tokyo", await ReadStringAsync(reopenedContext,
            "SELECT Value FROM AppStates WHERE Key = 'LastCityId'"));
        Assert.Equal(1, await ReadUserVersionAsync(reopenedContext));
    }

    [Fact]
    public async Task Initialize_rejects_a_newer_schema_without_overwriting_its_version()
    {
        using var directory = new TemporaryDirectory();
        var factory = new AppDbContextFactory(Path.Combine(directory.Path, "weatherdesk.db"));
        Assert.True((await factory.InitializeAsync(CancellationToken.None)).IsSuccess);

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA user_version = 99");
        }

        var result = await factory.InitializeAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Storage, result.Failure!.Kind);
        await using var newerContext = await factory.CreateDbContextAsync();
        Assert.Equal(99, await ReadUserVersionAsync(newerContext));
    }

    [Fact]
    public async Task Initialize_maps_directory_errors_to_a_safe_storage_failure()
    {
        using var directory = new TemporaryDirectory();
        var blocker = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(blocker, "x");
        var factory = new AppDbContextFactory(Path.Combine(blocker, "weatherdesk.db"));

        var result = await factory.InitializeAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Storage, result.Failure!.Kind);
        Assert.DoesNotContain(blocker, result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialize_returns_a_canceled_result_when_the_token_is_canceled()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new AppDbContextFactory(Path.Combine(directory.Path, "weatherdesk.db"));

        var result = await factory.InitializeAsync(cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Canceled, result.Failure!.Kind);
        Assert.False(File.Exists(Path.Combine(directory.Path, "weatherdesk.db")));
    }

    private static async Task<List<string>> ReadStringsAsync(AppDbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<string> ReadStringAsync(AppDbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> ReadUserVersionAsync(AppDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadPrimaryKeyColumnAsync(AppDbContext context, string tableName)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetInt32(5) == 1)
            {
                return reader.GetString(1);
            }
        }

        throw new InvalidOperationException($"No primary key found for {tableName}.");
    }

    private static async Task<List<DatabaseIndex>> ReadIndexesAsync(AppDbContext context, string tableName)
    {
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        var indexDefinitions = new List<(string Name, bool IsUnique)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list('{tableName}')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexDefinitions.Add((reader.GetString(1), reader.GetInt32(2) != 0));
            }
        }

        var indexes = new List<DatabaseIndex>();
        foreach (var (name, isUnique) in indexDefinitions)
        {
            var columns = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_info('{name}')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(2));
            }

            indexes.Add(new DatabaseIndex(isUnique, columns));
        }

        return indexes;
    }

    private sealed record DatabaseIndex(bool IsUnique, IReadOnlyList<string> Columns);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
