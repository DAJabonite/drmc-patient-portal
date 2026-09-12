using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<QueueTicket> QueueTickets => Set<QueueTicket>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<AssistanceProgram> AssistancePrograms => Set<AssistanceProgram>();
    public DbSet<PublicAdvisory> PublicAdvisories => Set<PublicAdvisory>();
    
    // Authenticated Clinical Features (Phase 3.2)
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<LabResultItem> LabResultItems => Set<LabResultItem>();
    public DbSet<ClinicalEncounter> ClinicalEncounters => Set<ClinicalEncounter>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<MedicationDoseSchedule> MedicationDoseSchedules => Set<MedicationDoseSchedule>();
    public DbSet<PatientAllergy> PatientAllergies => Set<PatientAllergy>();
    public DbSet<RefillRequest> RefillRequests => Set<RefillRequest>();
    public DbSet<TriageIntake> TriageIntakes => Set<TriageIntake>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PatientIdDocument> PatientIdDocuments => Set<PatientIdDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Appointment>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.Appointments)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Doctor)
                .WithMany(d => d.Appointments)
                .HasForeignKey(x => x.DoctorId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.BookingReference).IsUnique();
        });

        builder.Entity<QueueTicket>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.QueueTickets)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.TicketNumber);
            e.HasIndex(x => x.Department);
        });

        builder.Entity<Doctor>(e =>
        {
            e.HasIndex(x => x.Department);
        });

        builder.Entity<AssistanceProgram>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<PublicAdvisory>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<LabResult>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.LabResults)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Encounter)
                .WithMany(x => x.LabResults)
                .HasForeignKey(x => x.ClinicalEncounterId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.PatientUserId);
            e.HasIndex(x => x.ClinicalEncounterId);
            e.HasIndex(x => x.AccessionNumber);
        });

        builder.Entity<ClinicalEncounter>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(x => x.Encounters)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.PatientUserId);
            e.HasIndex(x => x.EncounterReference).IsUnique();
        });

        builder.Entity<LabResultItem>(e =>
        {
            e.HasOne(x => x.LabResult)
                .WithMany(r => r.Items)
                .HasForeignKey(x => x.LabResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Prescription>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.Prescriptions)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PatientUserId);
            e.HasIndex(x => x.RxNumber).IsUnique();
        });

        builder.Entity<MedicationDoseSchedule>(e =>
        {
            e.HasOne(x => x.Prescription)
                .WithMany(x => x.DoseSchedules)
                .HasForeignKey(x => x.PrescriptionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PrescriptionId, x.DoseTime }).IsUnique();
        });

        builder.Entity<PatientAllergy>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.Allergies)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PatientUserId);
        });

        builder.Entity<RefillRequest>(e =>
        {
            e.HasOne(x => x.Prescription)
                .WithMany(p => p.RefillRequests)
                .HasForeignKey(x => x.PrescriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PatientUserId);
        });

        builder.Entity<TriageIntake>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.TriageIntakes)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Appointment)
                .WithMany()
                .HasForeignKey(x => x.AppointmentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PatientUserId);
            e.HasIndex(x => x.AppointmentId).IsUnique();
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Timestamp);
        });

        builder.Entity<PatientIdDocument>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.IdDocuments)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PatientUserId);
            e.HasIndex(x => x.IdType);
            e.HasIndex(x => x.CapturedAt);
        });
    }
}
