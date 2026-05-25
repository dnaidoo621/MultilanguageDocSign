using LinguaSign.Analysis.Domain;
using LinguaSign.Analysis.Rules;
using Xunit;

namespace LinguaSign.Tests.Unit;

public class RiskRulesTests
{
    [Theory]
    [InlineData("This contract will automatically renew unless cancelled", RiskLevel.High, "AUTO_RENEWAL")]
    [InlineData("A penalty of two months' rent applies on early exit", RiskLevel.High, "PENALTY")]
    [InlineData("The employee agrees to a non-compete for two years", RiskLevel.High, "NON_COMPETE")]
    [InlineData("The landlord shall not be liable for any damages", RiskLevel.High, "LIABILITY")]
    [InlineData("Disputes shall be resolved by arbitration", RiskLevel.Medium, "ARBITRATION")]
    [InlineData("Either party may terminate with 30 days notice", RiskLevel.Medium, "TERMINATION")]
    [InlineData("A security deposit is required upfront", RiskLevel.Medium, "DEPOSIT")]
    [InlineData("All information shall remain confidential", RiskLevel.Low, "CONFIDENTIALITY")]
    [InlineData("This agreement is bound by the governing law of Korea", RiskLevel.Low, "GOVERNING_LAW")]
    public void Detects_known_risk_clauses(string text, RiskLevel level, string type)
    {
        var (detectedLevel, detectedType) = RiskRules.Detect(text);
        Assert.Equal(level, detectedLevel);
        Assert.Equal(type, detectedType);
    }

    [Theory]
    [InlineData("The working hours are 40 per week")]
    [InlineData("")]
    [InlineData(null)]
    public void Returns_none_for_unremarkable_or_empty_text(string? text)
    {
        var (level, type) = RiskRules.Detect(text);
        Assert.Equal(RiskLevel.None, level);
        Assert.Equal("NONE", type);
    }

    [Fact]
    public void Detection_is_case_insensitive()
    {
        var (level, _) = RiskRules.Detect("THIS WILL AUTOMATICALLY RENEW");
        Assert.Equal(RiskLevel.High, level);
    }

    [Fact]
    public void High_risk_wins_over_lower_when_both_present()
    {
        // Contains both a penalty (high) and confidentiality (low); high must win.
        var (level, type) = RiskRules.Detect("A penalty applies and all information stays confidential");
        Assert.Equal(RiskLevel.High, level);
        Assert.Equal("PENALTY", type);
    }
}
