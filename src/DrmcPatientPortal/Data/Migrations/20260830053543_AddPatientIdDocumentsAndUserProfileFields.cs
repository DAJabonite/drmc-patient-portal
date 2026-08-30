using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientIdDocumentsAndUserProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BloodType",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sex",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatientIdDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    IdType = table.Column<string>(type: "TEXT", nullable: false),
                    IdNumber = table.Column<string>(type: "TEXT", nullable: false),
                    FrontPhotoFileName = table.Column<string>(type: "TEXT", nullable: true),
                    BackPhotoFileName = table.Column<string>(type: "TEXT", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OcrConfidence = table.Column<float>(type: "REAL", nullable: false),
                    IsManualEntry = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtractedFieldsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientIdDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientIdDocuments_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientIdDocuments_CapturedAt",
                table: "PatientIdDocuments",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PatientIdDocuments_IdType",
                table: "PatientIdDocuments",
                column: "IdType");

            migrationBuilder.CreateIndex(
                name: "IX_PatientIdDocuments_PatientUserId",
                table: "PatientIdDocuments",
                column: "PatientUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientIdDocuments");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BloodType",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Sex",
                table: "AspNetUsers");
        }
    }
}
