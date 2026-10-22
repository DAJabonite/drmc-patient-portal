using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductionReadinessState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fresh upgrades already preserved this column in the corrected migration.
            // Previously upgraded installations cannot reconstruct lost names here.
            migrationBuilder.Sql("IF COL_LENGTH(N'PatientRecords', N'LegacyFullName') IS NULL ALTER TABLE [PatientRecords] ADD [LegacyFullName] nvarchar(max) NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "PatientRecords",
                type: "nvarchar(max)",
                nullable: false,
                computedColumnSql: "CAST(CASE WHEN [LastName] = N'' OR [FirstName] = N'' THEN COALESCE([LegacyFullName], N'') ELSE [LastName] + N', ' + [FirstName] + CASE WHEN NULLIF([MiddleName], N'') IS NULL THEN N'' ELSE N' ' + [MiddleName] END + CASE WHEN NULLIF([Suffix], N'') IS NULL THEN N'' ELSE N' ' + [Suffix] END END AS nvarchar(max))",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(194)",
                oldMaxLength: 194,
                oldComputedColumnSql: "CAST(CASE WHEN [LastName] = N'' OR [FirstName] = N'' THEN N'' ELSE [LastName] + N', ' + [FirstName] + CASE WHEN NULLIF([MiddleName], N'') IS NULL THEN N'' ELSE N' ' + [MiddleName] END + CASE WHEN NULLIF([Suffix], N'') IS NULL THEN N'' ELSE N' ' + [Suffix] END END AS nvarchar(194))",
                oldStored: true);

            migrationBuilder.CreateTable(
                name: "AccountEmailThrottles",
                columns: table => new
                {
                    RecipientHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmailThrottles", x => new { x.RecipientHash, x.WindowStartUtc });
                });

            migrationBuilder.CreateTable(
                name: "DocumentProtectionStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastDocumentId = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    LastOwner = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    LastFile = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProtectedCanary = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentProtectionStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivateFileRetirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FirstUnreferencedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateFileRetirements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailThrottles_WindowStartUtc",
                table: "AccountEmailThrottles",
                column: "WindowStartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateFileRetirements_Kind_Owner_FileName",
                table: "PrivateFileRetirements",
                columns: new[] { "Kind", "Owner", "FileName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("THROW 51014, 'Production readiness state and preserved names must be retained. Roll back by restoring the coordinated pre-upgrade backup.', 1;");
        }
    }
}
