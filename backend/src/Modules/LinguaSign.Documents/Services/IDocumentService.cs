using LinguaSign.Documents.Contracts;

namespace LinguaSign.Documents.Services;

public interface IDocumentService
{
    Task<DocumentSummary> CreateAsync(string userId, string fileName, Stream content, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentSummary>> ListAsync(string userId, CancellationToken ct = default);
    Task<DocumentDetail?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<DocumentFile?> OpenFileAsync(string userId, Guid id, CancellationToken ct = default);
}

public record DocumentFile(Stream Stream, string FileName);
