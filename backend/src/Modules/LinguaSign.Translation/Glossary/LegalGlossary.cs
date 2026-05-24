namespace LinguaSign.Translation.Glossary;

/// <summary>
/// Deterministic legal-term mappings injected into the translation prompt to reduce
/// terminology drift and hallucination. Expand per language pair over time.
/// </summary>
public static class LegalGlossary
{
    private static readonly Dictionary<string, Dictionary<string, string>> Map = new()
    {
        ["ko->en"] = new()
        {
            ["준거법"] = "governing law",
            ["중재"] = "arbitration",
            ["면책"] = "indemnification",
            ["기밀유지"] = "confidentiality",
            ["불가항력"] = "force majeure",
            ["해지"] = "termination",
            ["해지권"] = "right of termination",
            ["보증금"] = "security deposit",
            ["위약금"] = "penalty / liquidated damages",
            ["자동갱신"] = "automatic renewal",
            ["갑"] = "Party A (e.g. the employer/landlord)",
            ["을"] = "Party B (e.g. the employee/tenant)",
        },
    };

    public static string Render(string? source, string target)
    {
        var key = $"{(source ?? "ko").ToLowerInvariant()}->{target.ToLowerInvariant()}";
        if (!Map.TryGetValue(key, out var terms) || terms.Count == 0)
            return "(no glossary terms for this language pair)";
        return string.Join("\n", terms.Select(kv => $"- \"{kv.Key}\" -> \"{kv.Value}\""));
    }
}
