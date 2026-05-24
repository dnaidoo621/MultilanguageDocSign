using LinguaSign.Documents.Services;
using LinguaSign.Translation.Domain;
using LinguaSign.Translation.Llm;
using LinguaSign.Translation.Persistence;
using Microsoft.Extensions.Logging;

namespace LinguaSign.Translation.Services;

public interface ITranslationProcessingService
{
    /// <summary>Runs the translation for a DocumentTranslation. Invoked by a background job.</summary>
    Task ProcessAsync(Guid translationId, CancellationToken ct = default);
}

public class TranslationProcessingService(
    TranslationDbContext db,
    IDocumentService documents,
    ILlmTranslator translator,
    ILogger<TranslationProcessingService> logger) : ITranslationProcessingService
{
    public async Task ProcessAsync(Guid translationId, CancellationToken ct = default)
    {
        var translation = await db.Translations.FindAsync([translationId], ct);
        if (translation is null)
        {
            logger.LogWarning("Translation job: {Id} not found", translationId);
            return;
        }

        try
        {
            translation.Status = TranslationStatus.Translating;
            translation.Model = translator.Model;
            translation.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var doc = await documents.GetAsync(translation.UserId, translation.DocumentId, ct)
                      ?? throw new InvalidOperationException("Source document not found.");

            var order = 0;
            foreach (var page in doc.Pages)
            {
                var items = page.Blocks
                    .Where(b => !string.IsNullOrWhiteSpace(b.Text))
                    .Select(b => new TranslationItem(b.Id, b.Text))
                    .ToList();
                if (items.Count == 0) continue;

                var translated = await translator.TranslateAsync(
                    items, translation.SourceLanguage, translation.TargetLanguage, ct);

                foreach (var b in page.Blocks)
                {
                    if (string.IsNullOrWhiteSpace(b.Text)) continue;
                    db.Segments.Add(new TranslationSegment
                    {
                        DocumentTranslationId = translation.Id,
                        SourceBlockId = b.Id,
                        PageNumber = page.Number,
                        Order = order++,
                        SourceText = b.Text,
                        // Fall back to source text if the model dropped a segment.
                        TranslatedText = translated.TryGetValue(b.Id, out var t) && !string.IsNullOrWhiteSpace(t)
                            ? t
                            : b.Text,
                    });
                }
            }

            translation.Status = TranslationStatus.Completed;
            translation.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Translation {Id} completed ({Lang}) using {Model}",
                translationId, translation.TargetLanguage, translation.Model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Translation failed for {Id}", translationId);
            translation.Status = TranslationStatus.Failed;
            translation.Error = ex.Message;
            translation.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}
