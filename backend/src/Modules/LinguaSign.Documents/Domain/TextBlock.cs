namespace LinguaSign.Documents.Domain;

/// <summary>
/// An extracted text block with its bounding box (page pixel coordinates, top-left origin).
/// These IDs/coordinates are the anchor for clause linking and bilingual alignment in later phases.
/// </summary>
public class TextBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentPageId { get; set; }

    /// <summary>Reading order within the page.</summary>
    public int Order { get; set; }

    public string Text { get; set; } = default!;
    public string? Language { get; set; }
    public double Confidence { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double BoxWidth { get; set; }
    public double BoxHeight { get; set; }

    public DocumentPage Page { get; set; } = default!;
}
