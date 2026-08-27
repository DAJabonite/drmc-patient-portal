using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLabResultEncounterLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClinicalEncounterId",
                table: "LabResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_ClinicalEncounterId",
                table: "LabResults",
                column: "ClinicalEncounterId");

            migrationBuilder.AddForeignKey(
                name: "FK_LabResults_ClinicalEncounters_ClinicalEncounterId",
                table: "LabResults",
                column: "ClinicalEncounterId",
                principalTable: "ClinicalEncounters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LabResults_ClinicalEncounters_ClinicalEncounterId",
                table: "LabResults");

            migrationBuilder.DropIndex(
                name: "IX_LabResults_ClinicalEncounterId",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ClinicalEncounterId",
                table: "LabResults");
        }
    }
}
