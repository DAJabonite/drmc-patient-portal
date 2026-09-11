using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911024500_AddAccountLifecycleFoundation")]
public partial class AddAccountLifecycleFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AccountStatus",
            table: "AspNetUsers",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<DateTime>(
            name: "AccountStatusChangedAtUtc",
            table: "AspNetUsers",
            type: "TEXT",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddColumn<string>(name: "AccountStatusReason", table: "AspNetUsers", type: "TEXT", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<string>(name: "RegistrationReference", table: "AspNetUsers", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "PrivacyNoticeAcknowledgedAtUtc", table: "AspNetUsers", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "PrivacyNoticeVersion", table: "AspNetUsers", type: "TEXT", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<bool>(name: "OptionalConsentGranted", table: "AspNetUsers", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "OptionalConsentPurpose", table: "AspNetUsers", type: "TEXT", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "OptionalConsentRecordedAtUtc", table: "AspNetUsers", type: "TEXT", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUsers_RegistrationReference",
            table: "AspNetUsers",
            column: "RegistrationReference",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_AspNetUsers_RegistrationReference", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "AccountStatus", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "AccountStatusChangedAtUtc", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "AccountStatusReason", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "RegistrationReference", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "PrivacyNoticeAcknowledgedAtUtc", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "PrivacyNoticeVersion", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "OptionalConsentGranted", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "OptionalConsentPurpose", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "OptionalConsentRecordedAtUtc", table: "AspNetUsers");
    }
}
