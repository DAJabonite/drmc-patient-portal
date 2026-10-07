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
    public DbSet<RadiologyStudy> RadiologyStudies => Set<RadiologyStudy>();
    public DbSet<ClinicalEncounter> ClinicalEncounters => Set<ClinicalEncounter>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<MedicationDoseSchedule> MedicationDoseSchedules => Set<MedicationDoseSchedule>();
    public DbSet<PatientAllergy> PatientAllergies => Set<PatientAllergy>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<PatientIdDocument> PatientIdDocuments => Set<PatientIdDocument>();
    public DbSet<PatientRegistrationCode> PatientRegistrationCodes => Set<PatientRegistrationCode>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();

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
                     typeof(MedicationDoseSchedule), typeof(PatientAllergy), typeof(RadiologyStudy) })
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

        builder.Entity<PatientRegistrationCode>(e =>
        {
            e.HasOne(x => x.PatientRecord).WithMany()
                .HasForeignKey(x => x.PatientRecordId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CodeHash).HasMaxLength(32).IsFixedLength();
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.HasIndex(x => x.PatientRecordId);
            e.Property(x => x.CodeHint).HasMaxLength(PatientRegistrationCode.HintLength);
            e.Property(x => x.IssuingPoint).HasMaxLength(PatientRegistrationCode.IssuingPointLength);
            e.Property(x => x.IssuedById).HasMaxLength(450);
            e.Property(x => x.IssuedByEmail).HasMaxLength(256);
            e.Property(x => x.RedeemedByUserId).HasMaxLength(450);
        });

        builder.Entity<StaffInvitation>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(StaffInvitation.EmailLength);
            e.Property(x => x.NormalizedEmail).HasMaxLength(StaffInvitation.EmailLength);
            e.HasIndex(x => x.NormalizedEmail);
            e.Property(x => x.Role).HasMaxLength(StaffInvitation.RoleLength);
            e.Property(x => x.CodeHash).HasMaxLength(32).IsFixedLength();
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.Property(x => x.CodeHint).HasMaxLength(PatientRegistrationCode.HintLength);
            e.Property(x => x.InvitedById).HasMaxLength(450);
            e.Property(x => x.InvitedByEmail).HasMaxLength(256);
            e.Property(x => x.AcceptedByUserId).HasMaxLength(450);
            e.HasIndex(x => x.AcceptedByUserId);
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
            e.Property(x => x.ReportFileName).HasMaxLength(36);
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

        builder.Entity<RadiologyStudy>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(p => p.RadiologyStudies)
                .HasForeignKey(x => x.PatientRecordId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Encounter)
                .WithMany()
                .HasForeignKey(x => x.ClinicalEncounterId)
                .OnDelete(DeleteBehavior.ClientSetNull);
            // Accession numbers are unique ignoring case, matching the lab accession comparison.
            e.Property(x => x.AccessionNumber).HasMaxLength(RadiologyStudy.AccessionLength).UseCollation("Latin1_General_100_CI_AS");
            e.HasIndex(x => x.AccessionNumber).IsUnique();
            e.HasIndex(x => x.PatientRecordId);
            e.HasIndex(x => x.ClinicalEncounterId);
            e.HasIndex(x => new { x.Status, x.ReleasedAt });
            foreach (var name in new[] { nameof(RadiologyStudy.StudyName), nameof(RadiologyStudy.BodyRegion), nameof(RadiologyStudy.OrderingPhysician),
                         nameof(RadiologyStudy.RadiologistName), nameof(RadiologyStudy.PerformingUnit) })
                e.Property(name).HasMaxLength(RadiologyStudy.NameLength);
            foreach (var name in new[] { nameof(RadiologyStudy.ClinicalIndication), nameof(RadiologyStudy.Technique), nameof(RadiologyStudy.Comparison),
                         nameof(RadiologyStudy.AmendmentNote) })
                e.Property(name).HasMaxLength(RadiologyStudy.ShortTextLength);
            foreach (var name in new[] { nameof(RadiologyStudy.Impression), nameof(RadiologyStudy.PlainLanguageSummary), nameof(RadiologyStudy.InternalNotes) })
                e.Property(name).HasMaxLength(RadiologyStudy.ReportTextLength);
            e.Property(x => x.Findings).HasMaxLength(RadiologyStudy.FindingsLength);
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
