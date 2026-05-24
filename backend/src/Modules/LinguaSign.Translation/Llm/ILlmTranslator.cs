namespace LinguaSign.Translation.Llm;

public record TranslationItem(Guid BlockId, string Text);

/// <summary>
/// Translates a batch of source blocks (one page at a time) into the target language.
/// Returns block-id → translated text, preserving clause-level alignment.
/// </summary>
public interface ILlmTranslator
{
    /// <summary>Identifier of the underlying model (recorded for audit traceability).</summary>
    string Model { get; }

    Task<IReadOnlyDictionary<Guid, string>> TranslateAsync(
        IReadOnlyList<TranslationItem> blocks,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default);
}
