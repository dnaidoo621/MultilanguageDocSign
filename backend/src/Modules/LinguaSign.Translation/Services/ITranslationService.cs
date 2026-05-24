using LinguaSign.Translation.Contracts;

namespace LinguaSign.Translation.Services;

public interface ITranslationService
{
    /// <summary>Create (or reset) a translation for a document+language and return it as Pending.</summary>
    Task<TranslationSummary> StartAsync(string userId, Guid documentId, string targetLanguage, CancellationToken ct = default);

    Task<TranslationDetail?> GetAsync(string userId, Guid documentId, string targetLanguage, CancellationToken ct = default);
}
