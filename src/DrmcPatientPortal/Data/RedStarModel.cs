using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;
using DrmcPatientPortal.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DrmcPatientPortal.Data;

public static class RedStarModel
{
    public static void Configure(ModelBuilder builder, RedStarPhiProtection? protection = null)
    {
        foreach (var table in RedStarPhiProtection.Tables)
        {
            var entity = builder.Entity(table.Entity);
            entity.Property<int>("PhiProtectionVersion").HasDefaultValue(0);
            foreach (var field in table.Fields)
            {
                var converter = protection?.Converter(table.Entity.Name, field) ?? new ValueConverter<string, string>(
                    value => MissingProtection(value), value => MissingProtection(value));
                var property = entity.Property<string>(field).HasConversion(converter).HasColumnType("nvarchar(max)");
                property.Metadata.SetMaxLength(null);
            }
        }
        builder.Entity<RedStarProtectionState>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.BlindKeyCheck).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        });
        builder.Entity<RedStarPatient>(e =>
        {
            foreach (var field in new[] { "PatientCodeBlindIndex", "HospitalNumberBlindIndex", "PortalUserIdBlindIndex" })
                e.Property<string>(field).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.HasIndex(x => x.PatientCodeBlindIndex).IsUnique().HasFilter("[PatientCodeBlindIndex] IS NOT NULL");
            e.HasIndex(x => x.HospitalNumberBlindIndex);
            e.HasIndex(x => x.PortalUserIdBlindIndex).IsUnique().HasFilter("[PortalUserIdBlindIndex] IS NOT NULL");
        });
        builder.Entity<RedStarEnrollment>(e =>
        {
            e.HasOne(x => x.Patient).WithMany(x => x.Enrollments).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Program }).IsUnique();
            foreach (var name in new[] { nameof(RedStarEnrollment.EnrolledDate), nameof(RedStarEnrollment.StartDate), nameof(RedStarEnrollment.StatusDate) }) e.Property<DateTime?>(name).HasColumnType("date");
        });
        builder.Entity<RedStarAppointment>(e =>
        {
            e.HasOne(x => x.Enrollment).WithMany(x => x.Appointments).HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.DueDate).HasColumnType("date");
            e.HasIndex(x => new { x.State, x.Kind, x.DueDate });
        });
        builder.Entity<RedStarDispense>(e =>
        {
            e.HasOne(x => x.Enrollment).WithMany(x => x.Dispenses).HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RedStarAppointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("[AppointmentId] IS NOT NULL");
            e.Property(x => x.DispensedDate).HasColumnType("date");
            e.Property(x => x.EstimatedSupplyEnd).HasColumnType("date");
        });
        builder.Entity<RedStarRescheduleRequest>(e =>
        {
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RequestedDate).HasColumnType("date");
            e.HasIndex(x => new { x.AppointmentId, x.AppointmentRevision }).IsUnique().HasFilter("[State] = 0");
        });
        builder.Entity<RedStarReminder>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.PortalUserIdBlindIndex).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            e.HasIndex(x => new { x.AppointmentId, x.AppointmentRevision, x.Stage, x.Channel, x.PortalUserIdBlindIndex })
                .IsUnique().HasFilter("[PortalUserIdBlindIndex] IS NOT NULL");
            e.HasIndex(x => x.PortalUserIdBlindIndex);
            e.HasIndex(x => new { x.State, x.NextAttemptUtc });
        });
        builder.Entity<RedStarImportBatch>().Property(x => x.Id).ValueGeneratedNever();
    }

    private static string MissingProtection(string value) => throw new InvalidOperationException("Private registry protection is not configured.");
}
