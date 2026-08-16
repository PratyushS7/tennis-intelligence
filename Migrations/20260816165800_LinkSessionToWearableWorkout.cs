using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TennisIntelligence.Migrations
{
    /// <inheritdoc />
    public partial class LinkSessionToWearableWorkout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExternalWorkoutId",
                table: "Sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExternalWorkoutId",
                table: "Sessions",
                column: "ExternalWorkoutId",
                unique: true,
                filter: "\"ExternalWorkoutId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_ExternalWorkouts_ExternalWorkoutId",
                table: "Sessions",
                column: "ExternalWorkoutId",
                principalTable: "ExternalWorkouts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_ExternalWorkouts_ExternalWorkoutId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_ExternalWorkoutId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ExternalWorkoutId",
                table: "Sessions");
        }
    }
}
