using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Data;

public static class RedStarModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<RedStarPatient>(e =>
        {
            e.HasIndex(x => x.PatientCode).IsUnique();
            e.HasIndex(x => x.PortalUserId).IsUnique().HasFilter("[PortalUserId] IS NOT NULL");
            e.HasOne(x => x.PortalUser).WithMany().HasForeignKey(x => x.PortalUserId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PortalUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.AppointmentId, x.AppointmentRevision, x.Stage, x.Channel, x.PortalUserId }).IsUnique();
            e.HasIndex(x => new { x.State, x.NextAttemptUtc });
        });
        builder.Entity<RedStarImportBatch>().Property(x => x.Id).ValueGeneratedNever();
    }
}
