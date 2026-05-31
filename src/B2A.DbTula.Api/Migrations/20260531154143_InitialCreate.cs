using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2A.DbTula.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    GoogleId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Databases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DbType = table.Column<string>(type: "text", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    ConnectionStringEncrypted = table.Column<string>(type: "text", nullable: false),
                    IsWriteAccount = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Databases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Databases_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComparisonRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDbId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetDbId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InitiatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    SyncScriptSafe = table.Column<string>(type: "text", nullable: true),
                    SyncScriptRisky = table.Column<string>(type: "text", nullable: true),
                    SyncScriptDestructive = table.Column<string>(type: "text", nullable: true),
                    SummaryJson = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComparisonRuns_Databases_SourceDbId",
                        column: x => x.SourceDbId,
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComparisonRuns_Databases_TargetDbId",
                        column: x => x.TargetDbId,
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComparisonRuns_Users_InitiatedById",
                        column: x => x.InitiatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncApplyLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedById = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TargetDbId = table.Column<Guid>(type: "uuid", nullable: false),
                    SqlExecuted = table.Column<string>(type: "text", nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorDetails = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncApplyLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncApplyLogs_ComparisonRuns_ComparisonRunId",
                        column: x => x.ComparisonRunId,
                        principalTable: "ComparisonRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SyncApplyLogs_Databases_TargetDbId",
                        column: x => x.TargetDbId,
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncApplyLogs_Users_AppliedById",
                        column: x => x.AppliedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRuns_InitiatedById",
                table: "ComparisonRuns",
                column: "InitiatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRuns_SourceDbId",
                table: "ComparisonRuns",
                column: "SourceDbId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRuns_TargetDbId",
                table: "ComparisonRuns",
                column: "TargetDbId");

            migrationBuilder.CreateIndex(
                name: "IX_Databases_CreatedById",
                table: "Databases",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SyncApplyLogs_AppliedById",
                table: "SyncApplyLogs",
                column: "AppliedById");

            migrationBuilder.CreateIndex(
                name: "IX_SyncApplyLogs_ComparisonRunId",
                table: "SyncApplyLogs",
                column: "ComparisonRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncApplyLogs_TargetDbId",
                table: "SyncApplyLogs",
                column: "TargetDbId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleId",
                table: "Users",
                column: "GoogleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncApplyLogs");

            migrationBuilder.DropTable(
                name: "ComparisonRuns");

            migrationBuilder.DropTable(
                name: "Databases");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
