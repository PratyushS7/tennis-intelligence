using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TennisIntelligence.Migrations
{
    /// <inheritdoc />
    public partial class AddWearableImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ExportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalRecords = table.Column<int>(type: "integer", nullable: false),
                    InsertedRecords = table.Column<int>(type: "integer", nullable: false),
                    UpdatedRecords = table.Column<int>(type: "integer", nullable: false),
                    UnchangedRecords = table.Column<int>(type: "integer", nullable: false),
                    RejectedRecords = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalWorkouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceRecordId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceApplication = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActivityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceLastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DistanceMeters = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    CaloriesKcal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    AverageHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    MaxHeartRateBpm = table.Column<int>(type: "integer", nullable: true),
                    LastImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalWorkouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalWorkouts_ImportBatches_LastImportBatchId",
                        column: x => x.LastImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalWorkouts_LastImportBatchId",
                table: "ExternalWorkouts",
                column: "LastImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalWorkouts_Source_SourceRecordId",
                table: "ExternalWorkouts",
                columns: new[] { "Source", "SourceRecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalWorkouts_StartedAt",
                table: "ExternalWorkouts",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_ImportedAt",
                table: "ImportBatches",
                column: "ImportedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalWorkouts");

            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}
