using LinguaSign.Analysis.Llm;
using LinguaSign.Analysis.Persistence;
using LinguaSign.Analysis.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Analysis;

/// <summary>
/// Analysis module — hybrid (deterministic rules + LLM) risk detection and clause explanations.
/// </summary>
public static class AnalysisModule
{
    public static IServiceCollection AddAnalysisModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AnalysisDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("Postgres")));

        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddScoped<IAnalysisProcessingService, AnalysisProcessingService>();

        services.AddHttpClient<IClauseAnalyzer, OllamaClauseAnalyzer>(client =>
        {
            var baseUrl = (config["Llm:BaseUrl"] ?? "http://localhost:11434/v1").TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        return services;
    }
}
