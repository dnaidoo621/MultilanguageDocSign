namespace LinguaSign.Documents.Domain;

/// <summary>An uploaded source document and its processing state.</summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning user (Supabase JWT `sub`).</summary>
    public string UserId { get; set; } = default!;

    public string FileName { get; set; } = default!;

    /// <summary>Key/path in the configured document storage.</summary>
    public string StoragePath { get; set; } = default!;

    /// <summary>SHA-256 of the original bytes — audit traceability.</summary>
    public string ContentHash { get; set; } = default!;

    public long SizeBytes { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    /// <summary>Dominant detected source language (set after OCR).</summary>
    public string? SourceLanguage { get; set; }

    public int PageCount { get; set; }

    /// <summary>Failure detail when Status == Failed.</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<DocumentPage> Pages { get; set; } = [];
}
