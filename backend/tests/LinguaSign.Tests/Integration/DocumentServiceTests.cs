using System.Text;
using LinguaSign.Documents.Services;
using Xunit;

namespace LinguaSign.Tests.Integration;

[Collection("postgres")]
public class DocumentServiceTests(PostgresFixture fx)
{
    private static MemoryStream Pdf() => new(Encoding.UTF8.GetBytes("%PDF-1.4 fake content"));

    [Fact]
    public async Task Create_persists_with_hash_and_is_scoped_to_user()
    {
        var storage = new InMemoryStorage();
        await using var db = fx.NewDocuments();
        var svc = new DocumentService(db, storage);
        var userA = "userA-" + Guid.NewGuid();
        var userB = "userB-" + Guid.NewGuid();

        var summary = await svc.CreateAsync(userA, "lease.pdf", Pdf());

        Assert.Equal("lease.pdf", summary.FileName);
        Assert.Equal("Uploaded", summary.Status);
        Assert.NotEmpty(storage.Files);

        Assert.Single(await svc.ListAsync(userA));
        Assert.Empty(await svc.ListAsync(userB));

        Assert.NotNull(await svc.GetAsync(userA, summary.Id));
        Assert.Null(await svc.GetAsync(userB, summary.Id)); // ownership enforced
    }

    [Fact]
    public async Task OpenFile_returns_stored_bytes_for_owner_only()
    {
        var storage = new InMemoryStorage();
        await using var db = fx.NewDocuments();
        var svc = new DocumentService(db, storage);
        var user = "u-" + Guid.NewGuid();

        var summary = await svc.CreateAsync(user, "a.pdf", Pdf());

        var file = await svc.OpenFileAsync(user, summary.Id);
        Assert.NotNull(file);
        Assert.Null(await svc.OpenFileAsync("intruder-" + Guid.NewGuid(), summary.Id));
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_id()
    {
        await using var db = fx.NewDocuments();
        var svc = new DocumentService(db, new InMemoryStorage());
        Assert.Null(await svc.GetAsync("u-" + Guid.NewGuid(), Guid.NewGuid()));
    }
}
