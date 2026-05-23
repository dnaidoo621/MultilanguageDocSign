namespace LinguaSign.Documents.Contracts;

public record DocumentSummary(
    Guid Id,
    string FileName,
    string Status,
    string? SourceLanguage,
    int PageCount,
    DateTimeOffset CreatedAt);

public record DocumentDetail(
    Guid Id,
    string FileName,
    string Status,
    string? SourceLanguage,
    int PageCount,
    string? Error,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PageDto> Pages);

public record PageDto(int Number, double Width, double Height, IReadOnlyList<BlockDto> Blocks);

public record BlockDto(
    Guid Id,
    int Order,
    string Text,
    string? Language,
    double Confidence,
    double X,
    double Y,
    double Width,
    double Height);
