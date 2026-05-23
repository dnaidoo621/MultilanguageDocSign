using LinguaSign.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinguaSign.Documents.Persistence;

public class LinguaSignDbContext(DbContextOptions<LinguaSignDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();
    public DbSet<TextBlock> TextBlocks => Set<TextBlock>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("documents");

        b.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).IsRequired();
            e.Property(x => x.FileName).IsRequired();
            e.Property(x => x.StoragePath).IsRequired();
            e.Property(x => x.ContentHash).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Pages)
                .WithOne(p => p.Document)
                .HasForeignKey(p => p.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DocumentPage>(e =>
        {
            e.ToTable("document_pages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DocumentId, x.PageNumber }).IsUnique();
            e.HasMany(x => x.Blocks)
                .WithOne(t => t.Page)
                .HasForeignKey(t => t.DocumentPageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TextBlock>(e =>
        {
            e.ToTable("text_blocks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).IsRequired();
            e.HasIndex(x => x.DocumentPageId);
        });
    }
}
