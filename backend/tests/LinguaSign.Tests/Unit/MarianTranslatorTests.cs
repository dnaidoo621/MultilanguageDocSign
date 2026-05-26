using LinguaSign.Translation.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LinguaSign.Tests.Unit;

public class MarianTranslatorTests
{
    private static MarianTranslator Make(string responseJson) =>
        new(TestHttp.TranslationClient(responseJson), NullLogger<MarianTranslator>.Instance);

    private static List<TranslationItem> Blocks(params string[] texts) =>
        texts.Select(t => new TranslationItem(Guid.NewGuid(), t)).ToList();

    [Fact]
    public async Task Maps_translations_by_guid_not_by_index()
    {
        var blocks = Blocks("준거법은 대한민국법을 따른다.", "중재는 서울에서 한다.");
        var b0 = blocks[0].BlockId;
        var b1 = blocks[1].BlockId;

        // Sidecar echoes back the real GUIDs — no positional index needed.
        var json = $$"""
        {
          "model": "opus-mt-ko-en",
          "translations": [
            {"id": "{{b0}}", "text": "The governing law shall be the law of Korea."},
            {"id": "{{b1}}", "text": "Arbitration shall take place in Seoul."}
          ]
        }
        """;

        var t = Make(json);
        var result = await t.TranslateAsync(blocks, "ko", "en");

        Assert.Equal("The governing law shall be the law of Korea.", result[b0]);
        Assert.Equal("Arbitration shall take place in Seoul.", result[b1]);
    }

    [Fact]
    public async Task Model_property_is_updated_from_sidecar_response()
    {
        var blocks = Blocks("테스트");
        var b0 = blocks[0].BlockId;
        var json = $$"""{"model":"opus-mt-ko-en","translations":[{"id":"{{b0}}","text":"Test"}]}""";

        var t = Make(json);
        Assert.Equal("opus-mt-ko-en", t.Model);   // default before first call

        await t.TranslateAsync(blocks, "ko", "en");

        Assert.Equal("opus-mt-ko-en", t.Model);   // updated from response
    }

    [Fact]
    public async Task Empty_block_list_returns_empty_without_making_a_network_call()
    {
        // The stub returns an error body — but it should never be reached for empty input.
        var t = Make("""{"error":"should not be called"}""");
        var result = await t.TranslateAsync(new List<TranslationItem>(), "ko", "en");
        Assert.Empty(result);
    }

    [Fact]
    public async Task Partial_response_returns_what_is_available_without_crashing()
    {
        var blocks = Blocks("A", "B");
        var b0 = blocks[0].BlockId;

        // Sidecar only returns one of the two blocks.
        var json = $$"""{"model":"opus-mt-ko-en","translations":[{"id":"{{b0}}","text":"Alpha"}]}""";

        var result = await Make(json).TranslateAsync(blocks, "ko", "en");

        Assert.Single(result);
        Assert.Equal("Alpha", result[b0]);
        // b1 is not in the result; TranslationProcessingService will fall back to source text.
    }

    [Fact]
    public async Task Malformed_guid_in_response_is_skipped_gracefully()
    {
        var blocks = Blocks("A");
        // The sidecar returns a non-GUID id — should not throw.
        var json = """{"model":"opus-mt-ko-en","translations":[{"id":"not-a-guid","text":"X"}]}""";

        var result = await Make(json).TranslateAsync(blocks, "ko", "en");

        Assert.Empty(result);
    }
}
