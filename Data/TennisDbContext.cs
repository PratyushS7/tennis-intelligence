using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Models;

namespace TennisIntelligence.Data;

public sealed class TennisDbContext : DbContext
{
    public TennisDbContext(DbContextOptions<TennisDbContext> options) : base(options) { }

    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<InteractionLog> InteractionLogs => Set<InteractionLog>();
    public DbSet<DevelopmentGoal> DevelopmentGoals => Set<DevelopmentGoal>();
    public DbSet<GoalCheckIn> GoalCheckIns => Set<GoalCheckIn>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ExternalWorkout> ExternalWorkouts => Set<ExternalWorkout>();
    public DbSet<ExternalDailySummary> ExternalDailySummaries => Set<ExternalDailySummary>();
    public DbSet<ExternalBodyMeasurement> ExternalBodyMeasurements => Set<ExternalBodyMeasurement>();

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

        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ImportedAt);
        });

        modelBuilder.Entity<ExternalWorkout>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Source, e.SourceRecordId }).IsUnique();
            entity.HasIndex(e => e.StartedAt);
            entity.Property(e => e.DistanceMeters).HasPrecision(12, 2);
            entity.Property(e => e.CaloriesKcal).HasPrecision(10, 2);
            entity.Property(e => e.HeartRateSamples).HasColumnType("jsonb");
            entity.Property(e => e.RawPayload).HasColumnType("jsonb");
            entity.HasOne(e => e.LastImportBatch)
                  .WithMany()
                  .HasForeignKey(e => e.LastImportBatchId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExternalDailySummary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Source, e.SummaryDate }).IsUnique();
            entity.Property(e => e.ActiveCaloriesKcal).HasPrecision(10, 2);
            entity.Property(e => e.TotalCaloriesKcal).HasPrecision(10, 2);
            entity.Property(e => e.DistanceMeters).HasPrecision(12, 2);
            entity.Property(e => e.HeartRateVariabilityRmssdMs).HasPrecision(8, 2);
            entity.Property(e => e.OxygenSaturationPercent).HasPrecision(5, 2);
            entity.Property(e => e.Vo2MaxMlPerKgPerMin).HasPrecision(5, 2);
            entity.Property(e => e.RawPayload).HasColumnType("jsonb");
            entity.HasOne(e => e.LastImportBatch)
                  .WithMany()
                  .HasForeignKey(e => e.LastImportBatchId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExternalBodyMeasurement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Source, e.SourceRecordId }).IsUnique();
            entity.HasIndex(e => e.MeasuredAt);
            entity.Property(e => e.WeightKg).HasPrecision(8, 3);
            entity.Property(e => e.BodyFatPercent).HasPrecision(5, 2);
            entity.Property(e => e.RawPayload).HasColumnType("jsonb");
            entity.HasOne(e => e.LastImportBatch)
                  .WithMany()
                  .HasForeignKey(e => e.LastImportBatchId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
