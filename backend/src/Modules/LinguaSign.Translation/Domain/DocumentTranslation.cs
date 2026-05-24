namespace LinguaSign.Translation.Domain;

/// <summary>A translation of a document into a target language.</summary>
public class DocumentTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Loose reference to a Documents-module document (no cross-context FK).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Owning user (denormalized for ownership checks).</summary>
    public string UserId { get; set; } = default!;

    public string TargetLanguage { get; set; } = default!;
    public string? SourceLanguage { get; set; }

    public TranslationStatus Status { get; set; } = TranslationStatus.Pending;

    /// <summary>LLM model used (audit traceability).</summary>
    public string? Model { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TranslationSegment> Segments { get; set; } = [];
}
