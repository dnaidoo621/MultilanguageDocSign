using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LinguaSign.Documents.Ocr;

/// <summary>HTTP client for the Python Surya OCR sidecar (POST /ocr, multipart PDF).</summary>
public class SuryaOcrClient(HttpClient http) : IOcrService
{
    public async Task<OcrResult> ExtractAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(pdf);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", fileName);

        using var resp = await http.PostAsync("/ocr", form, ct);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<SuryaResponse>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty OCR response from sidecar.");

        var pages = dto.Pages
            .Select(p => new OcrPage(
                p.Number,
                p.Width,
                p.Height,
                p.Blocks.Select(ToBlock).ToList()))
            .ToList();

        return new OcrResult(pages);
    }

    private static OcrBlock ToBlock(SuryaBlock b)
    {
        // Sidecar emits bbox as [x0, y0, x1, y1] in page pixels.
        var x0 = b.Bbox.ElementAtOrDefault(0);
        var y0 = b.Bbox.ElementAtOrDefault(1);
        var x1 = b.Bbox.ElementAtOrDefault(2);
        var y1 = b.Bbox.ElementAtOrDefault(3);
        return new OcrBlock(b.Text, b.Language, b.Confidence, new OcrBBox(x0, y0, x1 - x0, y1 - y0));
    }

    private record SuryaResponse(
        [property: JsonPropertyName("pages")] List<SuryaPage> Pages);

    private record SuryaPage(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("width")] double Width,
        [property: JsonPropertyName("height")] double Height,
        [property: JsonPropertyName("blocks")] List<SuryaBlock> Blocks);

    private record SuryaBlock(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("bbox")] double[] Bbox);
}
