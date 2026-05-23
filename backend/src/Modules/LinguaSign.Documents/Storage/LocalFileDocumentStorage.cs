using Microsoft.Extensions.Configuration;

namespace LinguaSign.Documents.Storage;

/// <summary>Filesystem-backed storage for local development.</summary>
public class LocalFileDocumentStorage : IDocumentStorage
{
    private readonly string _root;

    public LocalFileDocumentStorage(IConfiguration config)
    {
        var configured = config["Storage:LocalRoot"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "linguasign-storage")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(ResolvePath(key)));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolvePath(string key)
    {
        // Guard against path traversal.
        var safe = key.Replace("..", string.Empty).TrimStart('/', '\\');
        return Path.Combine(_root, safe);
    }
}
