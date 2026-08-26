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
    public DbSet<PatientAllergy> PatientAllergies => Set<PatientAllergy>();
    public DbSet<RefillRequest> RefillRequests => Set<RefillRequest>();
    public DbSet<TriageIntake> TriageIntakes => Set<TriageIntake>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<DependentProfile> DependentProfiles => Set<DependentProfile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ConsentLogEntry> ConsentLogEntries => Set<ConsentLogEntry>();

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

            e.HasIndex(x => x.PatientUserId);
            e.HasIndex(x => x.AccessionNumber);
        });

        builder.Entity<LabResultItem>(e =>
        {
            e.HasOne(x => x.LabResult)
                .WithMany(r => r.Items)
                .HasForeignKey(x => x.LabResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ClinicalEncounter>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.Encounters)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PatientUserId);
            e.HasIndex(x => x.EncounterReference).IsUnique();
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
            e.HasIndex(x => x.AppointmentId);
        });

        builder.Entity<MessageThread>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.MessageThreads)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PatientUserId);
        });

        builder.Entity<Message>(e =>
        {
            e.HasOne(x => x.Thread)
                .WithMany(t => t.Messages)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ThreadId);
        });

        builder.Entity<DependentProfile>(e =>
        {
            e.HasOne(x => x.Guardian)
                .WithMany(u => u.DependentProfiles)
                .HasForeignKey(x => x.GuardianUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.GuardianUserId);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Timestamp);
        });

        builder.Entity<ConsentLogEntry>(e =>
        {
            e.HasOne(x => x.Guardian)
                .WithMany(u => u.ConsentLogEntries)
                .HasForeignKey(x => x.GuardianUserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.GuardianUserId);
            e.HasIndex(x => x.Timestamp);
        });
    }
}
