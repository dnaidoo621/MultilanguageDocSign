namespace LinguaSign.Translation.Domain;

/// <summary>
/// A translated segment aligned to a source OCR block via <see cref="SourceBlockId"/>.
/// This linkage is what powers clause-level highlighting in the bilingual viewer.
/// </summary>
public class TranslationSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentTranslationId { get; set; }

    /// <summary>Loose reference to the source Documents-module text block.</summary>
    public Guid SourceBlockId { get; set; }

    public int PageNumber { get; set; }
    public int Order { get; set; }

    public string SourceText { get; set; } = default!;
    public string TranslatedText { get; set; } = default!;

    public DocumentTranslation Translation { get; set; } = default!;
}
