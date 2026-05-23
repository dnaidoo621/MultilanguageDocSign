using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Documents;

/// <summary>
/// Documents module — PDF upload, metadata, and storage orchestration.
/// Phase 1 will add upload handling, object-storage integration, and OCR job dispatch.
/// </summary>
public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // TODO Phase 1: register upload, storage, and OCR-orchestration services.
        return services;
    }
}
