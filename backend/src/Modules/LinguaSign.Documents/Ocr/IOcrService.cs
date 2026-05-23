namespace LinguaSign.Documents.Ocr;

/// <summary>
/// OCR + layout extraction. Implemented by <see cref="SuryaOcrClient"/> (self-hosted sidecar);
/// a cloud implementation (e.g. Azure Document Intelligence) can be swapped in via DI.
/// </summary>
public interface IOcrService
{
    Task<OcrResult> ExtractAsync(Stream pdf, string fileName, CancellationToken ct = default);
}
