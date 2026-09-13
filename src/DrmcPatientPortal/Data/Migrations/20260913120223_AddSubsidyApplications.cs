using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubsidyApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubsidyApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Need = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportedPayment = table.Column<int>(type: "INTEGER", nullable: false),
                    GovernmentIdReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClinicalDocumentReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    CostDocumentReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    IndigencyDocumentReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    Eligibility = table.Column<int>(type: "INTEGER", nullable: false),
                    Coverage = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmedPayment = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubsidyApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubsidyApplications_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubsidyApplications_PatientUserId",
                table: "SubsidyApplications",
                column: "PatientUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubsidyApplications");
        }
    }
}
