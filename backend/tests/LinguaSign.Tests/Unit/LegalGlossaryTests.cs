using LinguaSign.Translation.Glossary;
using Xunit;

namespace LinguaSign.Tests.Unit;

public class LegalGlossaryTests
{
    [Fact]
    public void Renders_ko_to_en_terms()
    {
        var rendered = LegalGlossary.Render("ko", "en");
        Assert.Contains("준거법", rendered);
        Assert.Contains("governing law", rendered);
        Assert.Contains("arbitration", rendered);
    }

    [Fact]
    public void Unknown_language_pair_returns_fallback_note()
    {
        var rendered = LegalGlossary.Render("xx", "yy");
        Assert.Contains("no glossary", rendered);
    }

    [Fact]
    public void Null_source_defaults_to_korean_glossary()
    {
        var rendered = LegalGlossary.Render(null, "en");
        Assert.Contains("준거법", rendered);
    }
}
