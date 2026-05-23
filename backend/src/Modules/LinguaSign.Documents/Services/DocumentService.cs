using System.Security.Cryptography;
using LinguaSign.Documents.Contracts;
using LinguaSign.Documents.Domain;
using LinguaSign.Documents.Persistence;
using LinguaSign.Documents.Storage;
using Microsoft.EntityFrameworkCore;

namespace LinguaSign.Documents.Services;

public class DocumentService(LinguaSignDbContext db, IDocumentStorage storage) : IDocumentService
{
    public async Task<DocumentSummary> CreateAsync(string userId, string fileName, Stream content, CancellationToken ct = default)
    {
        // Buffer once so we can hash, size, and hand a fresh stream to storage.
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var id = Guid.NewGuid();
        var key = $"{userId}/{id}/{fileName}";

        ms.Position = 0;
        await storage.SaveAsync(key, ms, ct);

        var doc = new Document
        {
            Id = id,
            UserId = userId,
            FileName = fileName,
            StoragePath = key,
            ContentHash = hash,
            SizeBytes = bytes.LongLength,
            Status = DocumentStatus.Uploaded,
        };

        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);

        return ToSummary(doc);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(string userId, CancellationToken ct = default)
    {
        var docs = await db.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return docs.Select(ToSummary).ToList();
    }

    public async Task<DocumentDetail?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var doc = await db.Documents
            .AsNoTracking()
            .Include(d => d.Pages.OrderBy(p => p.PageNumber))
                .ThenInclude(p => p.Blocks.OrderBy(b => b.Order))
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);

        if (doc is null) return null;

        var pages = doc.Pages
            .Select(p => new PageDto(
                p.PageNumber,
                p.Width,
                p.Height,
                p.Blocks.Select(b => new BlockDto(
                    b.Id, b.Order, b.Text, b.Language, b.Confidence,
                    b.X, b.Y, b.BoxWidth, b.BoxHeight)).ToList()))
            .ToList();

        return new DocumentDetail(
            doc.Id, doc.FileName, doc.Status.ToString(), doc.SourceLanguage,
            doc.PageCount, doc.Error, doc.CreatedAt, pages);
    }

    public async Task<DocumentFile?> OpenFileAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var doc = await db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);

        if (doc is null) return null;

        var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
        return new DocumentFile(stream, doc.FileName);
    }

    private static DocumentSummary ToSummary(Document d)
        => new(d.Id, d.FileName, d.Status.ToString(), d.SourceLanguage, d.PageCount, d.CreatedAt);
}
