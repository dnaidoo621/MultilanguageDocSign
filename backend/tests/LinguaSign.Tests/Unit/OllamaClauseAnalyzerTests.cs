using LinguaSign.Analysis.Domain;
using LinguaSign.Analysis.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LinguaSign.Tests.Unit;

public class OllamaClauseAnalyzerTests
{
    private static OllamaClauseAnalyzer Make(string modelContent) =>
        new(TestHttp.ChatClient(modelContent), new ConfigurationBuilder().Build(), NullLogger<OllamaClauseAnalyzer>.Instance);

    private static List<ClauseInput> Clauses(params string[] texts) =>
        texts.Select(t => new ClauseInput(Guid.NewGuid(), t)).ToList();

    [Fact]
    public async Task Parses_analyses_with_risk_and_explanation()
    {
        var clauses = Clauses("auto renew clause", "salary clause");
        var a = Make("""{"analyses":[{"id":1,"risk":"high","type":"AUTO_RENEWAL","explanation":"renews automatically"},{"id":2,"risk":"none","type":"NONE","explanation":"ok"}]}""");

        var result = await a.AnalyzeAsync(clauses);

        Assert.Equal(RiskLevel.High, result[clauses[0].BlockId].Level);
        Assert.Equal("AUTO_RENEWAL", result[clauses[0].BlockId].Type);
        Assert.Equal("renews automatically", result[clauses[0].BlockId].Explanation);
        Assert.Equal(RiskLevel.None, result[clauses[1].BlockId].Level);
    }

    [Theory]
    [InlineData("high", RiskLevel.High)]
    [InlineData("medium", RiskLevel.Medium)]
    [InlineData("med", RiskLevel.Medium)]
    [InlineData("low", RiskLevel.Low)]
    [InlineData("none", RiskLevel.None)]
    [InlineData("garbage", RiskLevel.None)]
    public async Task Maps_risk_level_strings(string risk, RiskLevel expected)
    {
        var clauses = Clauses("x");
        var a = Make($$"""{"analyses":[{"id":1,"risk":"{{risk}}","type":"T","explanation":"e"}]}""");

        var result = await a.AnalyzeAsync(clauses);

        Assert.Equal(expected, result[clauses[0].BlockId].Level);
    }

    [Fact]
    public async Task Malformed_json_returns_empty_so_caller_falls_back_to_rules()
    {
        var result = await Make("not json").AnalyzeAsync(Clauses("x"));
        Assert.Empty(result);
    }

    [Fact]
    public async Task Empty_clauses_returns_empty()
    {
        var result = await Make("""{"analyses":[]}""").AnalyzeAsync(new List<ClauseInput>());
        Assert.Empty(result);
    }
}
