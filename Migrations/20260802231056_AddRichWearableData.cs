using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TennisIntelligence.Migrations
{
    /// <inheritdoc />
    public partial class AddRichWearableData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HeartRateSampleCount",
                table: "ExternalWorkouts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HeartRateSamples",
                table: "ExternalWorkouts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "MinHeartRateBpm",
                table: "ExternalWorkouts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalBodyMeasurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceRecordId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceApplication = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceLastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: true),
                    BodyFatPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    LastImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalBodyMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalBodyMeasurements_ImportBatches_LastImportBatchId",
                        column: x => x.LastImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalDailySummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SummaryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Steps = table.Column<long>(type: "bigint", nullable: true),
                    ActiveCaloriesKcal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TotalCaloriesKcal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    DistanceMeters = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    RestingHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    HeartRateVariabilityRmssdMs = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    OxygenSaturationPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    SleepDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    AwakeMinutes = table.Column<int>(type: "integer", nullable: true),
                    LightSleepMinutes = table.Column<int>(type: "integer", nullable: true),
                    DeepSleepMinutes = table.Column<int>(type: "integer", nullable: true),
                    RemSleepMinutes = table.Column<int>(type: "integer", nullable: true),
                    LastImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalDailySummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalDailySummaries_ImportBatches_LastImportBatchId",
                        column: x => x.LastImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalBodyMeasurements_LastImportBatchId",
                table: "ExternalBodyMeasurements",
                column: "LastImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalBodyMeasurements_MeasuredAt",
                table: "ExternalBodyMeasurements",
                column: "MeasuredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalBodyMeasurements_Source_SourceRecordId",
                table: "ExternalBodyMeasurements",
                columns: new[] { "Source", "SourceRecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalDailySummaries_LastImportBatchId",
                table: "ExternalDailySummaries",
                column: "LastImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalDailySummaries_Source_SummaryDate",
                table: "ExternalDailySummaries",
                columns: new[] { "Source", "SummaryDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalBodyMeasurements");

            migrationBuilder.DropTable(
                name: "ExternalDailySummaries");

            migrationBuilder.DropColumn(
                name: "HeartRateSampleCount",
                table: "ExternalWorkouts");

            migrationBuilder.DropColumn(
                name: "HeartRateSamples",
                table: "ExternalWorkouts");

            migrationBuilder.DropColumn(
                name: "MinHeartRateBpm",
                table: "ExternalWorkouts");
        }
    }
}
