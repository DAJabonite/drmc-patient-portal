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
            // Preserve the exact original name before replacing the stored column.
            migrationBuilder.AddColumn<string>(name: "LegacyFullName", table: "PatientRecords", type: "nvarchar(max)", nullable: true);
            migrationBuilder.Sql("UPDATE [PatientRecords] SET [LegacyFullName] = [FullName];");
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
                type: "nvarchar(max)",
                nullable: false,
                computedColumnSql: "CAST(CASE WHEN [LastName] = N'' OR [FirstName] = N'' THEN COALESCE([LegacyFullName], N'') ELSE [LastName] + N', ' + [FirstName] + CASE WHEN NULLIF([MiddleName], N'') IS NULL THEN N'' ELSE N' ' + [MiddleName] END + CASE WHEN NULLIF([Suffix], N'') IS NULL THEN N'' ELSE N' ' + [Suffix] END END AS nvarchar(max))",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [PatientRecords] WHERE DATALENGTH([FirstName]) > 0 OR DATALENGTH([LastName]) > 0 OR DATALENGTH([MiddleName]) > 0 OR DATALENGTH([Suffix]) > 0) OR EXISTS (SELECT 1 FROM [AspNetUsers] WHERE DATALENGTH([Suffix]) > 0) THROW 51013, 'Structured patient names and account suffixes must be retained; downgrade is blocked.', 1;");

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

            migrationBuilder.Sql("UPDATE [PatientRecords] SET [FullName] = COALESCE([LegacyFullName], N'');");
            migrationBuilder.DropColumn(name: "LegacyFullName", table: "PatientRecords");

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
