namespace LinguaSign.Documents.Ocr;

/// <summary>Engine-agnostic OCR result. The sidecar response maps onto these.</summary>
public record OcrResult(IReadOnlyList<OcrPage> Pages);

public record OcrPage(int Number, double Width, double Height, IReadOnlyList<OcrBlock> Blocks);

public record OcrBlock(string Text, string? Language, double Confidence, OcrBBox BBox);

/// <summary>Bounding box in page pixel coordinates (top-left origin).</summary>
public record OcrBBox(double X, double Y, double Width, double Height);
