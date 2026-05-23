using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LinguaSign.Documents.Persistence;

/// <summary>
/// Used by `dotnet ef` at design time so migrations can be generated without the API host.
/// Override the connection with the LINGUASIGN_DB environment variable.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LinguaSignDbContext>
{
    public LinguaSignDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("LINGUASIGN_DB")
                   ?? "Host=localhost;Port=5432;Database=linguasign;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<LinguaSignDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new LinguaSignDbContext(options);
    }
}
