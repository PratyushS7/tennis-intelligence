using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Models;

namespace TennisIntelligence.Data;

public class TennisDbContext : DbContext
{
    public TennisDbContext(DbContextOptions<TennisDbContext> options) : base(options) { }

    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<InteractionLog> InteractionLogs => Set<InteractionLog>();
    public DbSet<DevelopmentGoal> DevelopmentGoals => Set<DevelopmentGoal>();
    public DbSet<GoalCheckIn> GoalCheckIns => Set<GoalCheckIn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Date).IsRequired();
            entity.Property(e => e.BreakdownAreas).HasMaxLength(500);
            entity.Property(e => e.BreakdownReasons).HasMaxLength(500);
            entity.Property(e => e.FocusArea).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.BodyFeel).HasMaxLength(20);

            entity.Ignore(e => e.BreakdownAreaList);
            entity.Ignore(e => e.BreakdownReasonList);
        });

        modelBuilder.Entity<InteractionLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.Action, e.Timestamp });
        });

        modelBuilder.Entity<DevelopmentGoal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasMany(e => e.CheckIns)
                  .WithOne(c => c.Goal)
                  .HasForeignKey(c => c.GoalId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoalCheckIn>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GoalId, e.SessionId }).IsUnique();
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.GoalCheckIns)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
