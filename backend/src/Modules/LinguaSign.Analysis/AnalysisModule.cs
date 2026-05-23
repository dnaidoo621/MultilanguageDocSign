using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Analysis;

/// <summary>
/// Analysis module — risk detection and clause explanation.
/// Phase 4 will add hybrid (deterministic rules + LLM) risk detection and explanation generation.
/// </summary>
public static class AnalysisModule
{
    public static IServiceCollection AddAnalysisModule(this IServiceCollection services)
    {
        // TODO Phase 4: register risk-detection and explanation services.
        return services;
    }
}
