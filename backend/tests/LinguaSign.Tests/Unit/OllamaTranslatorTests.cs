using LinguaSign.Translation.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LinguaSign.Tests.Unit;

public class OllamaTranslatorTests
{
    private static OllamaTranslator Make(string modelContent) =>
        new(TestHttp.ChatClient(modelContent), new ConfigurationBuilder().Build(), NullLogger<OllamaTranslator>.Instance);

    private static List<TranslationItem> Blocks(params string[] texts) =>
        texts.Select(t => new TranslationItem(Guid.NewGuid(), t)).ToList();

    [Fact]
    public async Task Parses_object_array_with_ids()
    {
        var blocks = Blocks("고용 계약서", "제1조");
        var t = Make("""{"translations":[{"id":1,"text":"Employment Contract"},{"id":2,"text":"Article 1"}]}""");

        var result = await t.TranslateAsync(blocks, "ko", "en");

        Assert.Equal("Employment Contract", result[blocks[0].BlockId]);
        Assert.Equal("Article 1", result[blocks[1].BlockId]);
    }

    [Fact]
    public async Task Parses_plain_string_array_by_position()
    {
        // The shape smaller models (e.g. qwen2.5:3b) emit — must not crash.
        var blocks = Blocks("A", "B");
        var t = Make("""{"translations":["Alpha","Beta"]}""");

        var result = await t.TranslateAsync(blocks, "ko", "en");

        Assert.Equal("Alpha", result[blocks[0].BlockId]);
        Assert.Equal("Beta", result[blocks[1].BlockId]);
    }

    [Fact]
    public async Task Supports_translation_alias_key()
    {
        var blocks = Blocks("A");
        var t = Make("""{"translations":[{"id":1,"translation":"Alpha"}]}""");

        var result = await t.TranslateAsync(blocks, "ko", "en");

        Assert.Equal("Alpha", result[blocks[0].BlockId]);
    }

    [Fact]
    public async Task Malformed_json_returns_empty()
    {
        var result = await Make("this is not json at all").TranslateAsync(Blocks("A"), "ko", "en");
        Assert.Empty(result);
    }

    [Fact]
    public async Task Missing_translations_key_returns_empty()
    {
        var result = await Make("""{"foo":123}""").TranslateAsync(Blocks("A"), "ko", "en");
        Assert.Empty(result);
    }

    [Fact]
    public async Task Empty_blocks_returns_empty_without_calling_model()
    {
        var result = await Make("""{"translations":[]}""").TranslateAsync(new List<TranslationItem>(), "ko", "en");
        Assert.Empty(result);
    }

    [Fact]
    public void Model_name_reads_from_configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Model"] = "qwen2.5:7b" })
            .Build();
        var t = new OllamaTranslator(TestHttp.ChatClient("{}"), config, NullLogger<OllamaTranslator>.Instance);
        Assert.Equal("qwen2.5:7b", t.Model);
    }
}
