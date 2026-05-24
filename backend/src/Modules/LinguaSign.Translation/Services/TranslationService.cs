using LinguaSign.Documents.Services;
using LinguaSign.Translation.Contracts;
using LinguaSign.Translation.Domain;
using LinguaSign.Translation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LinguaSign.Translation.Services;

public class TranslationService(TranslationDbContext db, IDocumentService documents) : ITranslationService
{
    public async Task<TranslationSummary> StartAsync(string userId, Guid documentId, string targetLanguage, CancellationToken ct = default)
    {
        var doc = await documents.GetAsync(userId, documentId, ct)
                  ?? throw new InvalidOperationException("Document not found.");

        if (!string.Equals(doc.Status, "Extracted", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Document is not ready for translation (status: {doc.Status}).");

        var existing = await db.Translations
            .FirstOrDefaultAsync(t => t.DocumentId == documentId && t.TargetLanguage == targetLanguage, ct);

        if (existing is null)
        {
            existing = new DocumentTranslation
            {
                DocumentId = documentId,
                UserId = userId,
                TargetLanguage = targetLanguage,
                SourceLanguage = doc.SourceLanguage,
                Status = TranslationStatus.Pending,
            };
            db.Translations.Add(existing);
        }
        else
        {
            // Reset for a re-run.
            await db.Segments.Where(s => s.DocumentTranslationId == existing.Id).ExecuteDeleteAsync(ct);
            existing.Status = TranslationStatus.Pending;
            existing.SourceLanguage = doc.SourceLanguage;
            existing.Error = null;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return ToSummary(existing);
    }

    public async Task<TranslationDetail?> GetAsync(string userId, Guid documentId, string targetLanguage, CancellationToken ct = default)
    {
        var t = await db.Translations
            .AsNoTracking()
            .Include(x => x.Segments.OrderBy(s => s.PageNumber).ThenBy(s => s.Order))
            .FirstOrDefaultAsync(
                x => x.DocumentId == documentId && x.TargetLanguage == targetLanguage && x.UserId == userId, ct);

        if (t is null) return null;

        var segments = t.Segments
            .Select(s => new TranslationSegmentDto(s.SourceBlockId, s.PageNumber, s.Order, s.SourceText, s.TranslatedText))
            .ToList();

        return new TranslationDetail(
            t.Id, t.DocumentId, t.TargetLanguage, t.SourceLanguage, t.Status.ToString(), t.Model, t.Error, segments);
    }

    private static TranslationSummary ToSummary(DocumentTranslation t)
        => new(t.Id, t.DocumentId, t.TargetLanguage, t.Status.ToString(), t.Model, t.CreatedAt);
}
