using LinguaSign.Documents.Contracts;
using LinguaSign.Documents.Services;
using LinguaSign.Translation.Services;
using NSubstitute;
using Xunit;

namespace LinguaSign.Tests.Integration;

[Collection("postgres")]
public class TranslationServiceTests(PostgresFixture fx)
{
    private static DocumentDetail Doc(Guid id, string status) =>
        new(id, "a.pdf", status, "ko", 1, null, DateTimeOffset.UtcNow, new List<PageDto>());

    [Fact]
    public async Task Start_creates_pending_when_document_extracted()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var docs = Substitute.For<IDocumentService>();
        docs.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns(Doc(docId, "Extracted"));

        await using var db = fx.NewTranslation();
        var svc = new TranslationService(db, docs);

        var summary = await svc.StartAsync(user, docId, "en");

        Assert.Equal("Pending", summary.Status);
        Assert.Equal("en", summary.TargetLanguage);
        var detail = await svc.GetAsync(user, docId, "en");
        Assert.NotNull(detail);
    }

    [Fact]
    public async Task Start_throws_when_document_not_extracted()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var docs = Substitute.For<IDocumentService>();
        docs.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns(Doc(docId, "Processing"));

        await using var db = fx.NewTranslation();
        var svc = new TranslationService(db, docs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync(user, docId, "en"));
    }

    [Fact]
    public async Task Start_throws_when_document_missing()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var docs = Substitute.For<IDocumentService>();
        docs.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns((DocumentDetail?)null);

        await using var db = fx.NewTranslation();
        var svc = new TranslationService(db, docs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync(user, docId, "en"));
    }

    [Fact]
    public async Task Get_returns_null_for_other_user()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var docs = Substitute.For<IDocumentService>();
        docs.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns(Doc(docId, "Extracted"));

        await using var db = fx.NewTranslation();
        var svc = new TranslationService(db, docs);
        await svc.StartAsync(user, docId, "en");

        Assert.Null(await svc.GetAsync("intruder-" + Guid.NewGuid(), docId, "en"));
    }
}
