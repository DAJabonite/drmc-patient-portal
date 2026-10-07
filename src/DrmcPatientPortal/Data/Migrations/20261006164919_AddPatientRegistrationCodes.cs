using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientRegistrationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientRegistrationCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientRecordId = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    CodeHint = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    IssuingPoint = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IssuedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IssuedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RedeemedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientRegistrationCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientRegistrationCodes_PatientRecords_PatientRecordId",
                        column: x => x.PatientRecordId,
                        principalTable: "PatientRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientRegistrationCodes_CodeHash",
                table: "PatientRegistrationCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientRegistrationCodes_PatientRecordId",
                table: "PatientRegistrationCodes",
                column: "PatientRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Codes record how a portal account was linked to a hospital record; never drop them silently.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [PatientRegistrationCodes]) THROW 51011, 'Registration code history must be retained; downgrade is blocked.', 1;");
            migrationBuilder.DropTable(
                name: "PatientRegistrationCodes");
        }
    }
}
