using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Configuration;

namespace WeatherDesk.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDbContextFactory<AppDbContext>
{
    public const int CommandTimeoutSeconds = 5;
    public const int CurrentSchemaVersion = 1;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public AppDbContextFactory()
        : this(AppPaths.DatabasePath)
    {
    }

    public AppDbContextFactory(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            DefaultTimeout = CommandTimeoutSeconds,
            Pooling = false
        }.ToString();
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString, sqlite => sqlite.CommandTimeout(CommandTimeoutSeconds))
            .Options;

        return new AppDbContext(options);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }

    public async Task<Result> InitializeAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Result.Fail(Failure.Canceled("操作已取消。"));
        }

        try
        {
            var databaseDirectory = Path.GetDirectoryName(_databasePath);
            if (string.IsNullOrWhiteSpace(databaseDirectory))
            {
                return StorageFailure();
            }

            Directory.CreateDirectory(databaseDirectory);
            await using var context = CreateDbContext();
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            var currentVersion = await ReadUserVersionAsync(context, cancellationToken).ConfigureAwait(false);
            if (currentVersion > CurrentSchemaVersion)
            {
                return StorageFailure();
            }

            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            if (currentVersion < CurrentSchemaVersion)
            {
                await context.Database.ExecuteSqlRawAsync(
                    $"PRAGMA user_version = {CurrentSchemaVersion}", cancellationToken).ConfigureAwait(false);
            }

            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Fail(Failure.Canceled("操作已取消。"));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return StorageFailure();
        }
    }

    private static async Task<int> ReadUserVersionAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or InvalidOperationException or TimeoutException or
            DbException or DbUpdateException;

    private static Result StorageFailure() =>
        Result.Fail(Failure.Storage("本地数据库不可用。"));
}
