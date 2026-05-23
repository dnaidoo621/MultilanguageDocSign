namespace LinguaSign.Documents.Storage;

/// <summary>
/// Abstraction over document blob storage. Local-file impl for dev;
/// a Supabase Storage / S3 impl swaps in for hosted environments.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>Persist content under <paramref name="key"/>; returns the stored key.</summary>
    Task<string> SaveAsync(string key, Stream content, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
