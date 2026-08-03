using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisIntelligence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailySummaryFreshness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceExportedAt",
                table: "ExternalDailySummaries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceExportedAt",
                table: "ExternalDailySummaries");
        }
    }
}
