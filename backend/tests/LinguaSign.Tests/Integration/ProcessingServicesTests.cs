using LinguaSign.Analysis.Domain;
using LinguaSign.Analysis.Llm;
using LinguaSign.Analysis.Services;
using LinguaSign.Audit.Services;
using LinguaSign.Documents.Contracts;
using LinguaSign.Documents.Domain;
using LinguaSign.Documents.Ocr;
using LinguaSign.Documents.Services;
using LinguaSign.Translation.Domain;
using LinguaSign.Translation.Llm;
using LinguaSign.Translation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace LinguaSign.Tests.Integration;

[Collection("postgres")]
public class ProcessingServicesTests(PostgresFixture fx)
{
    // ---------- OCR processing ----------

    [Fact]
    public async Task Ocr_processing_persists_blocks_and_sets_extracted()
    {
        var user = "u-" + Guid.NewGuid();
        await using var db = fx.NewDocuments();
        var storage = new InMemoryStorage();
        var doc = new Document { UserId = user, FileName = "a.pdf", StoragePath = $"{user}/a.pdf", ContentHash = "h", Status = DocumentStatus.Uploaded };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        storage.Files[doc.StoragePath] = "%PDF"u8.ToArray();

        var ocr = Substitute.For<IOcrService>();
        ocr.ExtractAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new OcrResult(new List<OcrPage>
            {
                new(1, 1191, 1684, new List<OcrBlock> { new("고용 계약서", "ko", 0.98, new OcrBBox(10, 10, 100, 30)) }),
            }));

        var svc = new OcrProcessingService(db, storage, ocr, NullLogger<OcrProcessingService>.Instance);
        await svc.ProcessAsync(doc.Id);

        await using var verify = fx.NewDocuments();
        var saved = await verify.Documents.Include(d => d.Pages).ThenInclude(p => p.Blocks).FirstAsync(d => d.Id == doc.Id);
        Assert.Equal(DocumentStatus.Extracted, saved.Status);
        Assert.Equal(1, saved.PageCount);
        Assert.Equal("ko", saved.SourceLanguage);
        Assert.Single(saved.Pages);
        Assert.Single(saved.Pages[0].Blocks);
        Assert.Equal("고용 계약서", saved.Pages[0].Blocks[0].Text);
    }

    [Fact]
    public async Task Ocr_processing_marks_failed_when_engine_throws()
    {
        var user = "u-" + Guid.NewGuid();
        await using var db = fx.NewDocuments();
        var storage = new InMemoryStorage();
        var doc = new Document { UserId = user, FileName = "a.pdf", StoragePath = $"{user}/a.pdf", ContentHash = "h", Status = DocumentStatus.Uploaded };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        storage.Files[doc.StoragePath] = "%PDF"u8.ToArray();

        var ocr = Substitute.For<IOcrService>();
        ocr.ExtractAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("engine down"));

        var svc = new OcrProcessingService(db, storage, ocr, NullLogger<OcrProcessingService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ProcessAsync(doc.Id));

        await using var verify = fx.NewDocuments();
        var saved = await verify.Documents.FirstAsync(d => d.Id == doc.Id);
        Assert.Equal(DocumentStatus.Failed, saved.Status);
        Assert.NotNull(saved.Error);
    }

    // ---------- Translation processing ----------

    [Fact]
    public async Task Translation_processing_writes_segments_and_completes()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        await using var db = fx.NewTranslation();
        var translation = new DocumentTranslation { DocumentId = docId, UserId = user, TargetLanguage = "en", SourceLanguage = "ko", Status = TranslationStatus.Pending };
        db.Translations.Add(translation);
        await db.SaveChangesAsync();

        var documents = Substitute.For<IDocumentService>();
        documents.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns(
            new DocumentDetail(docId, "a.pdf", "Extracted", "ko", 1, null, DateTimeOffset.UtcNow,
                new List<PageDto> { new(1, 100, 200, new List<BlockDto> { new(blockId, 0, "고용 계약서", "ko", 0.9, 0, 0, 0, 0) }) }));

        var translator = Substitute.For<ILlmTranslator>();
        translator.Model.Returns("test-model");
        translator.TranslateAsync(Arg.Any<IReadOnlyList<TranslationItem>>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [blockId] = "Employment Contract" });

        var svc = new TranslationProcessingService(db, documents, translator, NullLogger<TranslationProcessingService>.Instance);
        await svc.ProcessAsync(translation.Id);

        await using var verify = fx.NewTranslation();
        var saved = await verify.Translations.Include(t => t.Segments).FirstAsync(t => t.Id == translation.Id);
        Assert.Equal(TranslationStatus.Completed, saved.Status);
        Assert.Single(saved.Segments);
        Assert.Equal("Employment Contract", saved.Segments[0].TranslatedText);
    }

    // ---------- Analysis processing (hybrid rules + LLM) ----------

    [Fact]
    public async Task Analysis_processing_rules_override_llm_for_high_risk()
    {
        var user = "u-" + Guid.NewGuid();
        var docId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        await using var db = fx.NewAnalysis();
        var analysis = new DocumentAnalysis { DocumentId = docId, UserId = user, Status = AnalysisStatus.Pending };
        db.Analyses.Add(analysis);
        await db.SaveChangesAsync();

        var documents = Substitute.For<IDocumentService>();
        documents.GetAsync(user, docId, Arg.Any<CancellationToken>()).Returns(
            new DocumentDetail(docId, "a.pdf", "Extracted", "en", 1, null, DateTimeOffset.UtcNow,
                new List<PageDto> { new(1, 100, 200, new List<BlockDto> { new(blockId, 0, "A penalty of two months rent applies", null, 0.9, 0, 0, 0, 0) }) }));

        var translations = Substitute.For<ITranslationService>();
        translations.GetAsync(user, docId, "en", Arg.Any<CancellationToken>())
            .Returns((LinguaSign.Translation.Contracts.TranslationDetail?)null); // fall back to source text

        // LLM says no risk; deterministic rules must still flag PENALTY as High.
        var analyzer = Substitute.For<IClauseAnalyzer>();
        analyzer.Model.Returns("test-model");
        analyzer.AnalyzeAsync(Arg.Any<IReadOnlyList<ClauseInput>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ClauseAnalysis>());

        var audit = Substitute.For<IAuditService>();

        var svc = new AnalysisProcessingService(db, documents, translations, analyzer, audit, NullLogger<AnalysisProcessingService>.Instance);
        await svc.ProcessAsync(analysis.Id);

        await using var verify = fx.NewAnalysis();
        var saved = await verify.Analyses.Include(a => a.Findings).FirstAsync(a => a.Id == analysis.Id);
        Assert.Equal(AnalysisStatus.Completed, saved.Status);
        var finding = Assert.Single(saved.Findings);
        Assert.Equal(RiskLevel.High, finding.RiskLevel);
        Assert.Equal("PENALTY", finding.RiskType);
        await audit.Received().RecordAsync(user, docId, "RiskAnalyzed", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
