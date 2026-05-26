using LinguaSign.Translation.Llm;
using LinguaSign.Translation.Persistence;
using LinguaSign.Translation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Translation;

/// <summary>
/// Translation module — clause segmentation, glossary injection, LLM translation,
/// and source/target block alignment for the bilingual viewer.
/// </summary>
public static class TranslationModule
{
    public static IServiceCollection AddTranslationModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<TranslationDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("Postgres")));

        services.AddScoped<ITranslationService, TranslationService>();
        services.AddScoped<ITranslationProcessingService, TranslationProcessingService>();

        // Engine selection: Translation:Engine = "marian" (default) | "ollama" (fallback).
        // Switch back to Ollama at any time with: Translation:Engine=ollama
        var engine = config["Translation:Engine"] ?? "marian";

        if (string.Equals(engine, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            // Original Ollama path — kept so reverting is a one-line config change.
            services.AddHttpClient<ILlmTranslator, OllamaTranslator>(client =>
            {
                // Trailing slash is required so relative request paths keep the "/v1" segment.
                var baseUrl = (config["Llm:BaseUrl"] ?? "http://localhost:11434/v1").TrimEnd('/') + "/";
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromMinutes(10);
            });
        }
        else
        {
            // MarianMT sidecar path (default) — ~500 MB RAM, ~2–4 s/page.
            // The sidecar must be running on Translation:SidecarUrl (default :8001).
            services.AddHttpClient<ILlmTranslator, MarianTranslator>(client =>
            {
                var sidecarUrl = (config["Translation:SidecarUrl"] ?? "http://localhost:8001").TrimEnd('/') + "/";
                client.BaseAddress = new Uri(sidecarUrl);
                client.Timeout = TimeSpan.FromMinutes(5);
            });
        }

        return services;
    }
}
