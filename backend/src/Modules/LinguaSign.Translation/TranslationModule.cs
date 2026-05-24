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

        services.AddHttpClient<ILlmTranslator, OllamaTranslator>(client =>
        {
            // Trailing slash is required so relative request paths keep the "/v1" segment.
            var baseUrl = (config["Llm:BaseUrl"] ?? "http://localhost:11434/v1").TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        return services;
    }
}
