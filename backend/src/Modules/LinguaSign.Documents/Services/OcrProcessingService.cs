using LinguaSign.Documents.Domain;
using LinguaSign.Documents.Ocr;
using LinguaSign.Documents.Persistence;
using LinguaSign.Documents.Storage;
using Microsoft.Extensions.Logging;

namespace LinguaSign.Documents.Services;

public interface IOcrProcessingService
{
    /// <summary>Runs OCR for a document and persists its pages/blocks. Invoked by a background job.</summary>
    Task ProcessAsync(Guid documentId, CancellationToken ct = default);
}

public class OcrProcessingService(
    LinguaSignDbContext db,
    IDocumentStorage storage,
    IOcrService ocr,
    ILogger<OcrProcessingService> logger) : IOcrProcessingService
{
    public async Task ProcessAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.Documents.FindAsync([documentId], ct);
        if (doc is null)
        {
            logger.LogWarning("OCR job: document {DocumentId} not found", documentId);
            return;
        }

        try
        {
            doc.Status = DocumentStatus.Processing;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await using var file = await storage.OpenReadAsync(doc.StoragePath, ct);
            var result = await ocr.ExtractAsync(file, doc.FileName, ct);

            foreach (var p in result.Pages)
            {
                var page = new DocumentPage
                {
                    DocumentId = doc.Id,
                    PageNumber = p.Number,
                    Width = p.Width,
                    Height = p.Height,
                };

                var order = 0;
                foreach (var b in p.Blocks)
                {
                    page.Blocks.Add(new TextBlock
                    {
                        Order = order++,
                        Text = b.Text,
                        Language = b.Language,
                        Confidence = b.Confidence,
                        X = b.BBox.X,
                        Y = b.BBox.Y,
                        BoxWidth = b.BBox.Width,
                        BoxHeight = b.BBox.Height,
                    });
                }

                db.DocumentPages.Add(page);
            }

            doc.PageCount = result.Pages.Count;
            doc.SourceLanguage = DominantLanguage(result);
            doc.Status = DocumentStatus.Extracted;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "OCR complete for {DocumentId}: {Pages} pages, language {Language}",
                documentId, doc.PageCount, doc.SourceLanguage ?? "unknown");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OCR processing failed for document {DocumentId}", documentId);
            doc.Status = DocumentStatus.Failed;
            doc.Error = ex.Message;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw; // let Hangfire record the failure / retry
        }
    }

    private static string? DominantLanguage(OcrResult result)
        => result.Pages
            .SelectMany(p => p.Blocks)
            .Where(b => !string.IsNullOrWhiteSpace(b.Language))
            .GroupBy(b => b.Language!)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
}
