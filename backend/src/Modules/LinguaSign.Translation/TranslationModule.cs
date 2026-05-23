using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Translation;

/// <summary>
/// Translation module — clause segmentation, glossary injection, LLM translation,
/// and source/target block alignment.
/// Phase 2 will add the translation pipeline (local Ollama and/or cloud LLM routing).
/// </summary>
public static class TranslationModule
{
    public static IServiceCollection AddTranslationModule(this IServiceCollection services)
    {
        // TODO Phase 2: register segmentation, glossary, translation, and alignment services.
        return services;
    }
}
