using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Export;

/// <summary>
/// Export module — signed PDF, bilingual review copy, and audit package generation.
/// Uses PdfSharp/QuestPDF (not iText) for PDF work. Phase 3 will add export rendering.
/// </summary>
public static class ExportModule
{
    public static IServiceCollection AddExportModule(this IServiceCollection services)
    {
        // TODO Phase 3: register export/rendering services.
        return services;
    }
}
