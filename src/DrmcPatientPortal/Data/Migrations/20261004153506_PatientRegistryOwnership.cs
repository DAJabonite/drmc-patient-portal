using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations;

public partial class PatientRegistryOwnership : Migration
{
    private static readonly string[] ClinicalTables =
        ["ClinicalEncounters", "LabResults", "Prescriptions", "PatientAllergies"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PatientRecords",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                HospitalNumber = table.Column<string>(type: "nvarchar(450)", maxLength: 450,
                    nullable: true, collation: "Latin1_General_100_CI_AS"),
                PortalUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatientRecords", x => x.Id);
                table.ForeignKey("FK_PatientRecords_AspNetUsers_PortalUserId", x => x.PortalUserId,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.SetNull);
            });
        migrationBuilder.CreateIndex("IX_PatientRecords_PortalUserId", "PatientRecords", "PortalUserId",
            unique: true, filter: "[PortalUserId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_PatientRecords_HospitalNumber", "PatientRecords", "HospitalNumber",
            unique: true, filter: "[HospitalNumber] IS NOT NULL");

        foreach (var table in ClinicalTables)
            migrationBuilder.AddColumn<int>("PatientRecordId", table, type: "int", nullable: true);

        // Preserve the original owner until every clinical row has its replacement owner.
        migrationBuilder.Sql("""
            IF @@TRANCOUNT = 0
                THROW 51000, 'Patient ownership migration requires a transaction.', 1;

            INSERT INTO [PatientRecords] ([FullName], [PortalUserId])
            SELECT CASE WHEN NULLIF(LTRIM(RTRIM(u.[FullName])), N'') IS NOT NULL THEN u.[FullName]
                        ELSE LTRIM(RTRIM(COALESCE(u.[FirstName], N'') + N' ' + COALESCE(u.[LastName], N''))) END,
                   owners.[PatientUserId]
            FROM (
                SELECT [PatientUserId] FROM [ClinicalEncounters]
                UNION SELECT [PatientUserId] FROM [LabResults]
                UNION SELECT [PatientUserId] FROM [Prescriptions]
                UNION SELECT [PatientUserId] FROM [PatientAllergies]
            ) AS owners
            INNER JOIN [AspNetUsers] AS u ON u.[Id] = owners.[PatientUserId];

            UPDATE c SET [PatientRecordId] = p.[Id]
            FROM [ClinicalEncounters] AS c INNER JOIN [PatientRecords] AS p ON p.[PortalUserId] = c.[PatientUserId];
            UPDATE c SET [PatientRecordId] = p.[Id]
            FROM [LabResults] AS c INNER JOIN [PatientRecords] AS p ON p.[PortalUserId] = c.[PatientUserId];
            UPDATE c SET [PatientRecordId] = p.[Id]
            FROM [Prescriptions] AS c INNER JOIN [PatientRecords] AS p ON p.[PortalUserId] = c.[PatientUserId];
            UPDATE c SET [PatientRecordId] = p.[Id]
            FROM [PatientAllergies] AS c INNER JOIN [PatientRecords] AS p ON p.[PortalUserId] = c.[PatientUserId];

            IF EXISTS (SELECT 1 FROM [ClinicalEncounters] WHERE [PatientRecordId] IS NULL)
               OR EXISTS (SELECT 1 FROM [LabResults] WHERE [PatientRecordId] IS NULL)
               OR EXISTS (SELECT 1 FROM [Prescriptions] WHERE [PatientRecordId] IS NULL)
               OR EXISTS (SELECT 1 FROM [PatientAllergies] WHERE [PatientRecordId] IS NULL)
               OR EXISTS (SELECT 1 FROM [PatientRecords] WHERE NULLIF(LTRIM(RTRIM([FullName])), N'') IS NULL)
                THROW 51001, 'Patient ownership backfill is incomplete or a patient name is empty.', 1;
            """);

        foreach (var table in ClinicalTables)
        {
            migrationBuilder.AlterColumn<int>("PatientRecordId", table, type: "int", nullable: false,
                oldClrType: typeof(int), oldType: "int", oldNullable: true);
            migrationBuilder.CreateIndex($"IX_{table}_PatientRecordId", table, "PatientRecordId");
            migrationBuilder.AddForeignKey($"FK_{table}_PatientRecords_PatientRecordId", table,
                "PatientRecordId", "PatientRecords", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
        }
        foreach (var table in ClinicalTables)
        {
            migrationBuilder.DropForeignKey($"FK_{table}_AspNetUsers_PatientUserId", table);
            migrationBuilder.DropIndex($"IX_{table}_PatientUserId", table);
            migrationBuilder.DropColumn("PatientUserId", table);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Unlinked hospital records cannot be represented by the previous account-owned schema.
        migrationBuilder.Sql("""
            IF @@TRANCOUNT = 0
                THROW 51000, 'Patient ownership rollback requires a transaction.', 1;
            IF EXISTS (SELECT 1 FROM [ClinicalEncounters] AS c LEFT JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId] WHERE p.[PortalUserId] IS NULL)
               OR EXISTS (SELECT 1 FROM [LabResults] AS c LEFT JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId] WHERE p.[PortalUserId] IS NULL)
               OR EXISTS (SELECT 1 FROM [Prescriptions] AS c LEFT JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId] WHERE p.[PortalUserId] IS NULL)
               OR EXISTS (SELECT 1 FROM [PatientAllergies] AS c LEFT JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId] WHERE p.[PortalUserId] IS NULL)
                THROW 51002, 'Cannot roll back ownership while a clinical patient is unlinked.', 1;
            """);
        foreach (var table in ClinicalTables)
            migrationBuilder.AddColumn<string>("PatientUserId", table, type: "nvarchar(450)", nullable: true);

        migrationBuilder.Sql("""
            UPDATE c SET [PatientUserId] = p.[PortalUserId]
            FROM [ClinicalEncounters] AS c INNER JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId];
            UPDATE c SET [PatientUserId] = p.[PortalUserId]
            FROM [LabResults] AS c INNER JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId];
            UPDATE c SET [PatientUserId] = p.[PortalUserId]
            FROM [Prescriptions] AS c INNER JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId];
            UPDATE c SET [PatientUserId] = p.[PortalUserId]
            FROM [PatientAllergies] AS c INNER JOIN [PatientRecords] AS p ON p.[Id] = c.[PatientRecordId];

            IF EXISTS (SELECT 1 FROM [ClinicalEncounters] WHERE [PatientUserId] IS NULL)
               OR EXISTS (SELECT 1 FROM [LabResults] WHERE [PatientUserId] IS NULL)
               OR EXISTS (SELECT 1 FROM [Prescriptions] WHERE [PatientUserId] IS NULL)
               OR EXISTS (SELECT 1 FROM [PatientAllergies] WHERE [PatientUserId] IS NULL)
                THROW 51002, 'Patient ownership rollback is incomplete.', 1;
            """);
        foreach (var table in ClinicalTables)
        {
            migrationBuilder.AlterColumn<string>("PatientUserId", table, type: "nvarchar(450)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(450)", oldNullable: true);
            migrationBuilder.CreateIndex($"IX_{table}_PatientUserId", table, "PatientUserId");
            migrationBuilder.AddForeignKey($"FK_{table}_AspNetUsers_PatientUserId", table,
                "PatientUserId", "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        }
        foreach (var table in ClinicalTables)
        {
            migrationBuilder.DropForeignKey($"FK_{table}_PatientRecords_PatientRecordId", table);
            migrationBuilder.DropIndex($"IX_{table}_PatientRecordId", table);
            migrationBuilder.DropColumn("PatientRecordId", table);
        }
        migrationBuilder.DropTable("PatientRecords");
    }
}
