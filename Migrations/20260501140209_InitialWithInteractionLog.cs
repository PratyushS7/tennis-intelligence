using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TennisIntelligence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWithInteractionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InteractionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PageName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Metadata = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    EnergyLevel = table.Column<int>(type: "integer", nullable: false),
                    EnergyBefore = table.Column<string>(type: "text", nullable: true),
                    EnergyAfter = table.Column<string>(type: "text", nullable: true),
                    MatchFormat = table.Column<string>(type: "text", nullable: true),
                    ElbowPain = table.Column<int>(type: "integer", nullable: false),
                    ShoulderTightness = table.Column<int>(type: "integer", nullable: false),
                    BreakdownAreas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BreakdownReasons = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FocusArea = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FocusAchieved = table.Column<bool>(type: "boolean", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SessionRating = table.Column<int>(type: "integer", nullable: true),
                    SessionType = table.Column<string>(type: "text", nullable: true),
                    OpponentLevel = table.Column<string>(type: "text", nullable: true),
                    PlayStyle = table.Column<string>(type: "text", nullable: true),
                    MentalState = table.Column<string>(type: "text", nullable: true),
                    MatchResult = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionLogs_Action_Timestamp",
                table: "InteractionLogs",
                columns: new[] { "Action", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionLogs_Timestamp",
                table: "InteractionLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InteractionLogs");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
