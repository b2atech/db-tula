using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2A.DbTula.Api.Migrations
{
    /// <inheritdoc />
    public partial class Batch4Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CronExpression",
                table: "Profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchRunId",
                table: "ComparisonRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BatchRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TotalRuns = table.Column<int>(type: "integer", nullable: false),
                    CompletedRuns = table.Column<int>(type: "integer", nullable: false),
                    FailedRuns = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InitiatedById = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchRuns_Users_InitiatedById",
                        column: x => x.InitiatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchRuns_InitiatedById",
                table: "BatchRuns",
                column: "InitiatedById");

            migrationBuilder.CreateIndex(
                name: "IX_BatchRuns_StartedAt",
                table: "BatchRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchRuns");

            migrationBuilder.DropColumn(
                name: "CronExpression",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "BatchRunId",
                table: "ComparisonRuns");
        }
    }
}
