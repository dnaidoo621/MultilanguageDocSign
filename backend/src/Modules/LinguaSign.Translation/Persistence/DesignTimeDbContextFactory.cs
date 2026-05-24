using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LinguaSign.Translation.Persistence;

/// <summary>Design-time factory so `dotnet ef` can generate migrations without the API host.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TranslationDbContext>
{
    public TranslationDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("LINGUASIGN_DB")
                   ?? "Host=localhost;Port=5432;Database=linguasign;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<TranslationDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new TranslationDbContext(options);
    }
}
