using LinguaSign.Analysis.Services;
using LinguaSign.Documents.Contracts;
using LinguaSign.Documents.Services;
using NSubstitute;
using Xunit;

namespace LinguaSign.Tests.Integration;

[Collection("postgres")]
public class AnalysisServiceTests(PostgresFixture fx)
{
    private static DocumentDetail Doc(Guid id, string status) =>
        new(id, "a.pdf", status, "ko", 1, null, DateTimeOffset.UtcNow, new List<PageDto>());

    [Fact]
    public async Task Start_creates_pending_when_extracted()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var docs = Substitute.For<IDocumentService>();
        docs.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns(Doc(docId, "Extracted"));

        await using var db = fx.NewAnalysis();
        var svc = new AnalysisService(db, docs);

        var summary = await svc.StartAsync(user, docId);
        Assert.Equal("Pending", summary.Status);
        Assert.NotNull(await svc.GetAsync(user, docId));
    }

    [Fact]
    public async Task Start_throws_when_not_extracted()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var docs = Substitute.For<IDocumentService>();
        docs.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns(Doc(docId, "Uploaded"));

        await using var db = fx.NewAnalysis();
        var svc = new AnalysisService(db, docs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync(user, docId));
    }
}
