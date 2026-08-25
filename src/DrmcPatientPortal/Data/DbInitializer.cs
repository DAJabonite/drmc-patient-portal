using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Data;

// Seeds the database with a small number of patient accounts and the minimal
// rows the Home/Dashboard summary cards read from. This runs only in Development.
// The seeded accounts are documented in README.md; nothing "seed"-like is ever
// rendered in patient-facing UI text.
public static class DbInitializer
{
    public static void Initialize(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        // Ensure the schema exists so a fresh `dotnet run` works out of the box
        // (equivalent to `dotnet ef database update`).
        db.Database.Migrate();

        // If the seeded patient already exists, assume seeding was done and bail early.
        if (db.Users.Any(u => u.Email == "patient@drmc.doh.gov.ph"))
        {
            return;
        }

        var seededUsers = new List<(string email, string password, string firstName, string? middleName, string lastName, string contact, string idType, string idNumber)>
        {
            ("patient@drmc.doh.gov.ph", "P@tient2026", "Maria Clara", "D.", "Santos", "0917 123 4567", "Philippine National ID (PhilSys)", "1234-5678-9012-3456"),
            ("juan@drmc.doh.gov.ph", "J@uan2026",     "Juan Miguel",  "A.", "Dela Cruz", "0918 765 4321", "PhilHealth ID", "12-345678901-2"),
        };

        foreach (var (email, password, firstName, middleName, lastName, contact, idType, idNumber) in seededUsers)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName,
                MiddleName = middleName,
                LastName = lastName,
                FullName = string.IsNullOrWhiteSpace(middleName) ? $"{firstName} {lastName}" : $"{firstName} {middleName} {lastName}",
                ContactNumber = contact,
                PhoneNumber = contact,
                IdType = idType,
                IdNumber = idNumber,
                PrivacyConsent = true,
                CreatedAt = DateTime.UtcNow,
            };

            var result = userManager.CreateAsync(user, password).GetAwaiter().GetResult();
            if (!result.Succeeded)
            {
                // If the user already exists (e.g., re-run), just continue.
                continue;
            }
        }

        // --- Seeded dashboard data for the primary patient account ---
        var primary = db.Users.FirstOrDefault(u => u.Email == "patient@drmc.doh.gov.ph");
        if (primary is not null)
        {
            if (!db.NextAppointments.Any())
            {
                db.NextAppointments.AddRange(
                    new NextAppointment
                    {
                        PatientUserId = primary.Id,
                        Department = "Internal Medicine",
                        ScheduledAt = DateTime.Now.AddDays(12).Date.AddHours(9).AddMinutes(30),
                        Status = "Confirmed",
                        ProviderName = "Dr. A. Llanos",
                    },
                    new NextAppointment
                    {
                        PatientUserId = primary.Id,
                        Department = "Family & Community Medicine",
                        ScheduledAt = DateTime.Now.AddMonths(1).Date.AddHours(14),
                        Status = "Pending",
                        ProviderName = "Dr. C. Ramos",
                    });
            }

            if (!db.LabResults.Any())
            {
                db.LabResults.AddRange(
                    new LabResult
                    {
                        PatientUserId = primary.Id,
                        TestName = "Complete Blood Count (CBC)",
                        CollectedAt = DateTime.Now.AddDays(-6),
                        Status = "Available",
                        ResultSummary = "Your results are ready to review.",
                    },
                    new LabResult
                    {
                        PatientUserId = primary.Id,
                        TestName = "Fasting Blood Sugar (FBS)",
                        CollectedAt = DateTime.Now.AddDays(-2),
                        Status = "In progress",
                        ResultSummary = "Your test is still being processed.",
                    });
            }

            if (!db.Messages.Any())
            {
                db.Messages.AddRange(
                    new Message
                    {
                        PatientUserId = primary.Id,
                        RecipientUserId = primary.Id,
                        Subject = "Reminder: Upcoming appointment",
                        Body = "You have an appointment at Internal Medicine on your scheduled date. Please arrive 15 minutes early.",
                        SentAt = DateTime.Now.AddDays(-1),
                        IsRead = false,
                    },
                    new Message
                    {
                        PatientUserId = primary.Id,
                        RecipientUserId = primary.Id,
                        Subject = "Welcome",
                        Body = "Welcome to the DRMC patient portal.",
                        SentAt = DateTime.Now.AddDays(-10),
                        IsRead = true,
                    });
            }

            db.SaveChanges();
        }
    }
}
