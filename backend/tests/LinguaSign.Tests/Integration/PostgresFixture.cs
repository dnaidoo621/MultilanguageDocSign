using LinguaSign.Analysis.Persistence;
using LinguaSign.Audit.Persistence;
using LinguaSign.Documents.Persistence;
using LinguaSign.Documents.Storage;
using LinguaSign.Signing.Persistence;
using LinguaSign.Translation.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LinguaSign.Tests.Integration;

/// <summary>
/// Spins up an ephemeral database on the local Postgres (docker compose) for the test run,
/// migrates all module contexts, and drops it on teardown. Tests isolate by using unique user ids.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private const string AdminConn = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    private string _dbName = default!;
    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        _dbName = "linguasign_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(AdminConn))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", admin);
            await cmd.ExecuteNonQueryAsync();
        }
        ConnectionString = $"Host=localhost;Port=5432;Database={_dbName};Username=postgres;Password=postgres";

        await MigrateAndDispose(NewDocuments());
        await MigrateAndDispose(NewTranslation());
        await MigrateAndDispose(NewSigning());
        await MigrateAndDispose(NewAudit());
        await MigrateAndDispose(NewAnalysis());
    }

    private static async Task MigrateAndDispose(DbContext ctx)
    {
        await using (ctx) await ctx.Database.MigrateAsync();
    }

    private DbContextOptions<T> Opts<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(ConnectionString).Options;

    public LinguaSignDbContext NewDocuments() => new(Opts<LinguaSignDbContext>());
    public TranslationDbContext NewTranslation() => new(Opts<TranslationDbContext>());
    public SigningDbContext NewSigning() => new(Opts<SigningDbContext>());
    public AuditDbContext NewAudit() => new(Opts<AuditDbContext>());
    public AnalysisDbContext NewAnalysis() => new(Opts<AnalysisDbContext>());

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(AdminConn);
        await admin.OpenAsync();
        await using (var term = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_dbName}' AND pid <> pg_backend_pid()", admin))
            await term.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\"", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;

/// <summary>In-memory document storage double.</summary>
public sealed class InMemoryStorage : IDocumentStorage
{
    public readonly Dictionary<string, byte[]> Files = new();

    public Task<string> SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        Files[key] = ms.ToArray();
        return Task.FromResult(key);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream(Files[key]));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }
}
