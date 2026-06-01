using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2A.DbTula.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRuns_StartedAt",
                table: "ComparisonRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRuns_Status",
                table: "ComparisonRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ComparisonRuns_StartedAt",
                table: "ComparisonRuns");

            migrationBuilder.DropIndex(
                name: "IX_ComparisonRuns_Status",
                table: "ComparisonRuns");
        }
    }
}
