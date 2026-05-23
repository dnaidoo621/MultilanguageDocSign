namespace LinguaSign.Documents.Domain;

/// <summary>A single page of a document with its rendered pixel dimensions.</summary>
public class DocumentPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }

    public int PageNumber { get; set; }

    /// <summary>Page width in pixels at the resolution OCR was run (for bbox scaling).</summary>
    public double Width { get; set; }

    /// <summary>Page height in pixels at the resolution OCR was run.</summary>
    public double Height { get; set; }

    public Document Document { get; set; } = default!;
    public List<TextBlock> Blocks { get; set; } = [];
}
