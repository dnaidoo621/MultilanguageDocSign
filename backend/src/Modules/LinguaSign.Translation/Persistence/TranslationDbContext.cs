using LinguaSign.Translation.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinguaSign.Translation.Persistence;

public class TranslationDbContext(DbContextOptions<TranslationDbContext> options) : DbContext(options)
{
    public DbSet<DocumentTranslation> Translations => Set<DocumentTranslation>();
    public DbSet<TranslationSegment> Segments => Set<TranslationSegment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("translation");

        b.Entity<DocumentTranslation>(e =>
        {
            e.ToTable("document_translations");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).IsRequired();
            e.Property(x => x.TargetLanguage).IsRequired().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.DocumentId, x.TargetLanguage }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Segments)
                .WithOne(s => s.Translation)
                .HasForeignKey(s => s.DocumentTranslationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TranslationSegment>(e =>
        {
            e.ToTable("translation_segments");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceText).IsRequired();
            e.Property(x => x.TranslatedText).IsRequired();
            e.HasIndex(x => x.DocumentTranslationId);
            e.HasIndex(x => x.SourceBlockId);
        });
    }
}
