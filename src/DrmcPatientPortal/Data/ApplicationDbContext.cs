using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<PatientRecord> PatientRecords => Set<PatientRecord>();
    public DbSet<PublicAdvisory> PublicAdvisories => Set<PublicAdvisory>();
    
    // Patient clinical records
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<LabResultItem> LabResultItems => Set<LabResultItem>();
    public DbSet<ClinicalEncounter> ClinicalEncounters => Set<ClinicalEncounter>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<MedicationDoseSchedule> MedicationDoseSchedules => Set<MedicationDoseSchedule>();
    public DbSet<PatientAllergy> PatientAllergies => Set<PatientAllergy>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<PatientIdDocument> PatientIdDocuments => Set<PatientIdDocument>();

    private void GuardAuditHistory()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries().Any(e => (e.Entity is AuditLog or AdminAuditLog) &&
            e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit history is append-only.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAuditHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseCollation("Latin1_General_100_BIN2");

        builder.Entity<ImportBatch>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.ActorEmail).HasMaxLength(256);
            e.Property(x => x.ApprovedById).HasMaxLength(450);
            e.Property(x => x.ApprovedByEmail).HasMaxLength(256);
            e.Property(x => x.Template).HasMaxLength(50);
            e.Property(x => x.FileHash).HasMaxLength(64);
            e.Property(x => x.ValidationHash).HasMaxLength(64);
            e.Property(x => x.ApprovalHash).HasMaxLength(64);
            e.Property(x => x.ValidationVersion).HasMaxLength(50);
            e.Property(x => x.FailureCode).HasMaxLength(100);
            e.Property(x => x.OwnerId).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.Status, x.LeaseExpiresUtc });
        });

        foreach (var type in new[] { typeof(Doctor), typeof(PublicAdvisory), typeof(ClinicalEncounter),
                     typeof(LabResult), typeof(LabResultItem), typeof(Prescription),
                     typeof(MedicationDoseSchedule), typeof(PatientAllergy) })
            builder.Entity(type).Property<byte[]>("RowVersion").IsRequired().IsRowVersion();

        builder.Entity<AdminAuditLog>(e =>
        {
            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => x.SubjectPatientId);
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.ActorEmail).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.Entity).HasMaxLength(100);
            e.Property(x => x.RecordKey).HasMaxLength(450);
        });

        builder.Entity<PatientRecord>(e =>
        {
            e.Property(x => x.HospitalNumber).HasMaxLength(450).UseCollation("Latin1_General_100_CI_AS");
            e.HasIndex(x => x.HospitalNumber).IsUnique();
            e.HasIndex(x => x.PortalUserId).IsUnique();
            e.HasOne(x => x.PortalUser).WithOne(x => x.PatientRecord)
                .HasForeignKey<PatientRecord>(x => x.PortalUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Doctor>(e =>
        {
            e.HasIndex(x => x.Department);
        });

        builder.Entity<PublicAdvisory>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<LabResult>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.LabResults)
                .HasForeignKey(x => x.PatientRecordId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(x => x.Encounter)
                .WithMany(x => x.LabResults)
                .HasForeignKey(x => x.ClinicalEncounterId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            e.HasIndex(x => x.PatientRecordId);
            e.HasIndex(x => x.ClinicalEncounterId);
            e.HasIndex(x => x.AccessionNumber);
        });

        builder.Entity<ClinicalEncounter>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(x => x.Encounters)
                .HasForeignKey(x => x.PatientRecordId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.PatientRecordId);
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
                .HasForeignKey(x => x.PatientRecordId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(x => x.PatientRecordId);
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
                .HasForeignKey(x => x.PatientRecordId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(x => x.PatientRecordId);
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
