using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredPatientNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "PatientRecords",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "PatientRecords",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MiddleName",
                table: "PatientRecords",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suffix",
                table: "PatientRecords",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suffix",
                table: "AspNetUsers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "PatientRecords",
                type: "nvarchar(194)",
                maxLength: 194,
                nullable: false,
                computedColumnSql: "CAST(CASE WHEN [LastName] = N'' OR [FirstName] = N'' THEN N'' ELSE [LastName] + N', ' + [FirstName] + CASE WHEN NULLIF([MiddleName], N'') IS NULL THEN N'' ELSE N' ' + [MiddleName] END + CASE WHEN NULLIF([Suffix], N'') IS NULL THEN N'' ELSE N' ' + [Suffix] END END AS nvarchar(194))",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the computed column before dropping the parts it depends on.
            migrationBuilder.DropColumn(
                name: "FullName",
                table: "PatientRecords");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "PatientRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "PatientRecords");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "PatientRecords");

            migrationBuilder.DropColumn(
                name: "MiddleName",
                table: "PatientRecords");

            migrationBuilder.DropColumn(
                name: "Suffix",
                table: "PatientRecords");

            migrationBuilder.DropColumn(
                name: "Suffix",
                table: "AspNetUsers");

        }
    }
}
