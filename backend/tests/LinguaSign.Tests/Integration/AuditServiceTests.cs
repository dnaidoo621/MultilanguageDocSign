using LinguaSign.Audit.Services;
using Xunit;

namespace LinguaSign.Tests.Integration;

[Collection("postgres")]
public class AuditServiceTests(PostgresFixture fx)
{
    [Fact]
    public async Task Records_and_returns_trail_scoped_to_user()
    {
        await using var db = fx.NewAudit();
        var svc = new AuditService(db);
        var user = "u-" + Guid.NewGuid();
        var doc = Guid.NewGuid();

        await svc.RecordAsync(user, doc, "Signed", detail: "signed by X", documentHash: "abc123");
        await svc.RecordAsync(user, doc, "ExportGenerated");

        var trail = await svc.GetTrailAsync(user, doc);
        Assert.Equal(2, trail.Count);
        Assert.Contains(trail, e => e.EventType == "Signed" && e.DocumentHash == "abc123");
        Assert.Contains(trail, e => e.EventType == "ExportGenerated");

        // Other users / other docs see nothing.
        Assert.Empty(await svc.GetTrailAsync("other-" + Guid.NewGuid(), doc));
        Assert.Empty(await svc.GetTrailAsync(user, Guid.NewGuid()));
    }
}
