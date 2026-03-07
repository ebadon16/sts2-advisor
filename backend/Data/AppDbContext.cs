using Microsoft.EntityFrameworkCore;
using QuestceSpire.API.Models;

namespace QuestceSpire.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DbRun> Runs => Set<DbRun>();
    public DbSet<DbDecision> Decisions => Set<DbDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── DbRun ───
        modelBuilder.Entity<DbRun>(entity =>
        {
            entity.HasKey(e => e.RunId);

            entity.Property(e => e.RunId).HasMaxLength(64);
            entity.Property(e => e.PlayerId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Character).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Seed).HasMaxLength(64);
            entity.Property(e => e.Outcome).HasMaxLength(8).IsRequired();

            entity.HasIndex(e => e.PlayerId);
            entity.HasIndex(e => e.Character);
            entity.HasIndex(e => new { e.Character, e.Outcome });
        });

        // ─── DbDecision ───
        modelBuilder.Entity<DbDecision>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.RunId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ChosenId).HasMaxLength(128);
            entity.Property(e => e.OfferedIds).IsRequired();
            entity.Property(e => e.DeckSnapshot).IsRequired();
            entity.Property(e => e.RelicSnapshot).IsRequired();

            entity.HasIndex(e => e.RunId);
            entity.HasIndex(e => e.ChosenId);
            entity.HasIndex(e => e.EventType);

            entity.HasOne(e => e.Run)
                  .WithMany()
                  .HasForeignKey(e => e.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
