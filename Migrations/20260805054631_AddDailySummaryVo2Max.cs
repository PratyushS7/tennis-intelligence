using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisIntelligence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailySummaryVo2Max : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Vo2MaxMlPerKgPerMin",
                table: "ExternalDailySummaries",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Vo2MaxMlPerKgPerMin",
                table: "ExternalDailySummaries");
        }
    }
}
