using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace B2A.DbTula.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProfileId",
                table: "ComparisonRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AllowedEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    AddedById = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllowedEmails_Users_AddedById",
                        column: x => x.AddedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriftMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObjectType = table.Column<string>(type: "text", nullable: false),
                    MatchCount = table.Column<int>(type: "integer", nullable: false),
                    MismatchCount = table.Column<int>(type: "integer", nullable: false),
                    MissingInTargetCount = table.Column<int>(type: "integer", nullable: false),
                    MissingInSourceCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriftMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriftMetrics_ComparisonRuns_ComparisonRunId",
                        column: x => x.ComparisonRunId,
                        principalTable: "ComparisonRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SourceDbId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetDbId = table.Column<Guid>(type: "uuid", nullable: false),
                    IgnoreOwnership = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_Databases_SourceDbId",
                        column: x => x.SourceDbId,
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Profiles_Databases_TargetDbId",
                        column: x => x.TargetDbId,
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Profiles_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    ObjectType = table.Column<string>(type: "text", nullable: false),
                    ObjectName = table.Column<string>(type: "text", nullable: false),
                    Sql = table.Column<string>(type: "text", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    IsApplied = table.Column<bool>(type: "boolean", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncStatements_ComparisonRuns_ComparisonRunId",
                        column: x => x.ComparisonRunId,
                        principalTable: "ComparisonRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SyncStatements_Users_AppliedById",
                        column: x => x.AppliedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonRuns_ProfileId",
                table: "ComparisonRuns",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AllowedEmails_AddedById",
                table: "AllowedEmails",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_AllowedEmails_Email",
                table: "AllowedEmails",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriftMetrics_ComparisonRunId_ObjectType",
                table: "DriftMetrics",
                columns: new[] { "ComparisonRunId", "ObjectType" });

            migrationBuilder.CreateIndex(
                name: "IX_DriftMetrics_RunDate",
                table: "DriftMetrics",
                column: "RunDate");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_CreatedById",
                table: "Profiles",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_SourceDbId",
                table: "Profiles",
                column: "SourceDbId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_TargetDbId",
                table: "Profiles",
                column: "TargetDbId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncStatements_AppliedById",
                table: "SyncStatements",
                column: "AppliedById");

            migrationBuilder.CreateIndex(
                name: "IX_SyncStatements_ComparisonRunId_Category",
                table: "SyncStatements",
                columns: new[] { "ComparisonRunId", "Category" });

            migrationBuilder.AddForeignKey(
                name: "FK_ComparisonRuns_Profiles_ProfileId",
                table: "ComparisonRuns",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComparisonRuns_Profiles_ProfileId",
                table: "ComparisonRuns");

            migrationBuilder.DropTable(
                name: "AllowedEmails");

            migrationBuilder.DropTable(
                name: "DriftMetrics");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "SyncStatements");

            migrationBuilder.DropIndex(
                name: "IX_ComparisonRuns_ProfileId",
                table: "ComparisonRuns");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "ComparisonRuns");
        }
    }
}
