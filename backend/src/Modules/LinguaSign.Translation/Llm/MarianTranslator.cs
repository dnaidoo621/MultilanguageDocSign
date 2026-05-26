using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LinguaSign.Translation.Llm;

/// <summary>
/// Translates blocks via the LinguaSign translation sidecar (CTranslate2 / MarianMT).
///
/// Sends all blocks for one page in a single HTTP POST to /translate.
/// The sidecar echoes back the original block GUID as the translation ID,
/// so no positional index remapping is needed — each block maps cleanly by its own key.
///
/// The sidecar handles:
/// - CTranslate2 INT8 inference (~500 MB RAM, ~2–4 s/page vs Ollama's ~5–10 s/page)
/// - Legal-glossary constrained decoding (target_prefix + post-processing)
/// - Language-pair routing (multiple models loaded per language pair)
/// </summary>
public class MarianTranslator(
    HttpClient http,
    ILogger<MarianTranslator> logger) : ILlmTranslator
{
    // Initialised before the first call; updated from the first successful sidecar response.
    private string _model = "opus-mt-ko-en";

    public string Model => _model;

    public async Task<IReadOnlyDictionary<Guid, string>> TranslateAsync(
        IReadOnlyList<TranslationItem> blocks,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        if (blocks.Count == 0)
            return new Dictionary<Guid, string>();

        var src = (sourceLanguage ?? "ko").ToLowerInvariant();
        var tgt = targetLanguage.ToLowerInvariant();

        var request = new SidecarRequest(
            SourceLang: src,
            TargetLang: tgt,
            Items: blocks.Select(b => new SidecarItem(b.BlockId.ToString(), b.Text)).ToList()
        );

        using var resp = await http.PostAsJsonAsync("translate", request, ct);
        resp.EnsureSuccessStatusCode();

        var response = await resp.Content.ReadFromJsonAsync<SidecarResponse>(cancellationToken: ct);
        if (response is null)
        {
            logger.LogWarning("Empty response from translation sidecar");
            return new Dictionary<Guid, string>();
        }

        if (!string.IsNullOrWhiteSpace(response.Model))
            _model = response.Model;

        var result = new Dictionary<Guid, string>(response.Translations.Count);
        foreach (var t in response.Translations)
        {
            if (Guid.TryParse(t.Id, out var id) && !string.IsNullOrWhiteSpace(t.Text))
                result[id] = t.Text.Trim();
        }

        if (result.Count < blocks.Count)
            logger.LogWarning(
                "Translation sidecar returned {Got}/{Total} segments — missing blocks will fall back to source text",
                result.Count, blocks.Count);

        return result;
    }

    // ---- JSON contracts (sidecar-private, not exposed outside this class) ----

    private record SidecarRequest(
        [property: JsonPropertyName("source_lang")] string SourceLang,
        [property: JsonPropertyName("target_lang")] string TargetLang,
        [property: JsonPropertyName("items")] List<SidecarItem> Items);

    private record SidecarItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text);

    private record SidecarResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("translations")] List<SidecarTranslation> Translations);

    private record SidecarTranslation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text);
}
