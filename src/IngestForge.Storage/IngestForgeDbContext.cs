using Microsoft.EntityFrameworkCore;

namespace IngestForge.Storage;

public sealed class IngestForgeDbContext : DbContext
{
    public IngestForgeDbContext(DbContextOptions<IngestForgeDbContext> options) : base(options) { }

    public DbSet<ProcessedDocument> ProcessedDocuments => Set<ProcessedDocument>();
    public DbSet<PoisonDocument> PoisonDocuments => Set<PoisonDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var processed = modelBuilder.Entity<ProcessedDocument>();
        processed.ToTable("processed_documents");
        processed.HasKey(x => x.Id);
        processed.Property(x => x.Tenant).HasMaxLength(128).IsRequired();
        processed.Property(x => x.DedupKey).HasMaxLength(128).IsRequired();
        processed.Property(x => x.SourceUri).HasMaxLength(2048).IsRequired();
        processed.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        processed.Property(x => x.Title).HasMaxLength(1024);
        processed.Property(x => x.Author).HasMaxLength(512);
        processed.Property(x => x.Warnings).HasColumnType("text[]");
        // The dedup contract: one row per (tenant, dedup_key).
        processed.HasIndex(x => new { x.Tenant, x.DedupKey }).IsUnique();
        processed.HasIndex(x => x.ProcessedAt);

        var poison = modelBuilder.Entity<PoisonDocument>();
        poison.ToTable("poison_documents");
        poison.HasKey(x => x.Id);
        poison.Property(x => x.Tenant).HasMaxLength(128).IsRequired();
        poison.Property(x => x.DedupKey).HasMaxLength(128).IsRequired();
        poison.Property(x => x.SourceUri).HasMaxLength(2048).IsRequired();
        poison.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        poison.Property(x => x.LastError).HasMaxLength(4096).IsRequired();
        poison.HasIndex(x => new { x.Tenant, x.DedupKey }).IsUnique();
    }
}
