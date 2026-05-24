using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinguaSign.Translation.Glossary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LinguaSign.Translation.Llm;

/// <summary>
/// Translates via an OpenAI-compatible chat endpoint (Ollama by default, $0/doc locally).
/// Sends one page of blocks per call (never the whole document) with glossary injection.
/// </summary>
public class OllamaTranslator(HttpClient http, IConfiguration config, ILogger<OllamaTranslator> logger)
    : ILlmTranslator
{
    public string Model { get; } = config["Llm:Model"] ?? "qwen2.5:7b";

    public async Task<IReadOnlyDictionary<Guid, string>> TranslateAsync(
        IReadOnlyList<TranslationItem> blocks,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default)
    {
        if (blocks.Count == 0) return new Dictionary<Guid, string>();

        // Number blocks for the prompt; map indices back to GUIDs afterwards.
        var indexed = blocks.Select((b, i) => (Index: i + 1, b.BlockId, b.Text)).ToList();
        var itemsJson = JsonSerializer.Serialize(indexed.Select(x => new { id = x.Index, text = x.Text }));

        var system =
            "You are a careful legal-document translator. Translate faithfully and literally; " +
            "do NOT summarize, simplify, or omit anything. Preserve clause numbering and formatting, " +
            "keep obligations explicit, and maintain a formal tone. Use this glossary for the listed terms:\n" +
            LegalGlossary.Render(sourceLanguage, targetLanguage);

        var user =
            $"Translate each item's text into {LanguageName(targetLanguage)}. " +
            "Respond ONLY with JSON of the form {\"translations\":[{\"id\":<id>,\"text\":\"<translation>\"}]}, " +
            "one entry per input id.\n\nItems:\n" + itemsJson;

        var request = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            temperature = 0.1,
            response_format = new { type = "json_object" },
            stream = false,
        };

        // Relative path (no leading slash) so the base address's "/v1/" segment is preserved.
        using var resp = await http.PostAsJsonAsync("chat/completions", request, ct);
        resp.EnsureSuccessStatusCode();

        var completion = await resp.Content.ReadFromJsonAsync<ChatCompletion>(cancellationToken: ct);
        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content ?? "{}";

        var byIndex = ParseTranslations(content);
        var result = new Dictionary<Guid, string>();
        foreach (var x in indexed)
        {
            if (byIndex.TryGetValue(x.Index, out var t) && !string.IsNullOrWhiteSpace(t))
                result[x.BlockId] = t.Trim();
        }

        if (result.Count < indexed.Count)
            logger.LogWarning("Translator returned {Got}/{Total} segments", result.Count, indexed.Count);

        return result;
    }

    private static Dictionary<int, string> ParseTranslations(string content)
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("translations", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (!el.TryGetProperty("id", out var idEl) ||
                        !el.TryGetProperty("text", out var textEl))
                        continue;

                    var id = idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt32()
                        : int.TryParse(idEl.GetString(), out var p) ? p : -1;

                    if (id > 0) map[id] = textEl.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON response — leave map empty; caller falls back to source text.
        }
        return map;
    }

    private static string LanguageName(string code) => code.ToLowerInvariant() switch
    {
        "en" => "English",
        "ko" => "Korean",
        "af" => "Afrikaans",
        "zu" => "isiZulu",
        "xh" => "isiXhosa",
        "st" => "Sesotho",
        _ => code,
    };

    private record ChatCompletion([property: JsonPropertyName("choices")] List<Choice>? Choices);
    private record Choice([property: JsonPropertyName("message")] Message? Message);
    private record Message([property: JsonPropertyName("content")] string? Content);
}
