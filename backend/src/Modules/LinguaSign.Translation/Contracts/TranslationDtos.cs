namespace LinguaSign.Translation.Contracts;

public record TranslationSummary(
    Guid Id,
    Guid DocumentId,
    string TargetLanguage,
    string Status,
    string? Model,
    DateTimeOffset CreatedAt);

public record TranslationDetail(
    Guid Id,
    Guid DocumentId,
    string TargetLanguage,
    string? SourceLanguage,
    string Status,
    string? Model,
    string? Error,
    IReadOnlyList<TranslationSegmentDto> Segments);

public record TranslationSegmentDto(
    Guid SourceBlockId,
    int PageNumber,
    int Order,
    string SourceText,
    string TranslatedText);
