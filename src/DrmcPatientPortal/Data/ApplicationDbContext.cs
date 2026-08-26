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
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<Message> Messages => Set<Message>();

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
        });

        builder.Entity<Message>(e =>
        {
            e.HasOne(x => x.Patient)
                .WithMany(u => u.Messages)
                .HasForeignKey(x => x.PatientUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
