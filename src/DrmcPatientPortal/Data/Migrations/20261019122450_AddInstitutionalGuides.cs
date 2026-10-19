using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionalGuides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstitutionalGuideHeads",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LatestRevisionNumber = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionalGuideHeads", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionalGuideRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GuideKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SourceRevisionNumber = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EnglishJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FilipinoJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CebuanoJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionalGuideRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionalGuideRevisions_InstitutionalGuideHeads_GuideKey",
                        column: x => x.GuideKey,
                        principalTable: "InstitutionalGuideHeads",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "InstitutionalGuideHeads",
                columns: new[] { "Key", "LatestRevisionNumber" },
                values: new object[,]
                {
                    { "drmc-malasakit", 0 },
                    { "dswd", 0 },
                    { "maifip", 0 },
                    { "malasakit", 0 },
                    { "opd", 0 },
                    { "philhealth", 0 },
                    { "radiology-preparation", 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalGuideRevisions_GuideKey_EffectiveOn_RevisionNumber",
                table: "InstitutionalGuideRevisions",
                columns: new[] { "GuideKey", "EffectiveOn", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalGuideRevisions_GuideKey_RevisionNumber",
                table: "InstitutionalGuideRevisions",
                columns: new[] { "GuideKey", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstitutionalGuideRevisions");

            migrationBuilder.DropTable(
                name: "InstitutionalGuideHeads");
        }
    }
}
