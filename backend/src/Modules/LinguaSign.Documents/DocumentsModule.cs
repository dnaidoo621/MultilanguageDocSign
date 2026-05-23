using LinguaSign.Documents.Ocr;
using LinguaSign.Documents.Persistence;
using LinguaSign.Documents.Services;
using LinguaSign.Documents.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Documents;

/// <summary>
/// Documents module — PDF upload, metadata, storage, and OCR orchestration.
/// </summary>
public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<LinguaSignDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("Postgres")));

        services.AddScoped<IDocumentStorage, LocalFileDocumentStorage>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IOcrProcessingService, OcrProcessingService>();

        services.AddHttpClient<IOcrService, SuryaOcrClient>(client =>
        {
            client.BaseAddress = new Uri(config["Ocr:BaseUrl"] ?? "http://localhost:8000");
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        return services;
    }
}
