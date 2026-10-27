using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class RegistrationOutboxTests(PortalFactory factory)
{
    private sealed class RejectVerificationInsert : SaveChangesInterceptor
    {
        public string? UserId { get; private set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var message = eventData.Context?.ChangeTracker.Entries<AccountEmail>()
                .FirstOrDefault(e => e.State == EntityState.Added);
            if (message is not null)
            {
                UserId = message.Entity.UserId;
                Failures++;
                throw new IOException("Synthetic verification outbox persistence failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RegistrationDocuments(string root, IDataProtectionProvider protection) : IPatientDocumentStorage
    {
        public ConcurrentDictionary<(string UserId, string FileName), byte[]> Files { get; } = new();
        public int Created { get; private set; }

        public Task<StoredPatientDocument> CommitAsync(string token, string sessionId, string patientUserId,
            string side, CancellationToken cancellationToken = default)
        {
            var fileName = Guid.NewGuid().ToString("N");
            byte[] image = [137, 80, 78, 71, 13, 10, 26, 10];
            Files[(patientUserId, fileName)] = image;
            Directory.CreateDirectory(Path.Combine(root, patientUserId));
            File.WriteAllBytes(Path.Combine(root, patientUserId, fileName),
                protection.CreateProtector(KeyRingProtection.PatientDocumentPurpose).Protect(image));
            Created++;
            return Task.FromResult(new StoredPatientDocument(fileName, "image/png", 2));
        }

        public Task DeleteStoredAsync(string patientUserId, string? fileName, CancellationToken cancellationToken = default)
        {
            if (fileName is not null && Files.TryRemove((patientUserId, fileName), out _))
                File.Delete(Path.Combine(root, patientUserId, fileName));
            return Task.CompletedTask;
        }

        public Task<StagedPatientDocument> StageAsync(IFormFile file, string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredPatientDocument> StoreAsync(IFormFile file, string patientUserId, string side, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string patientUserId, string fileName, int storageVersion, CancellationToken cancellationToken = default) => Task.FromResult(Files[(patientUserId, fileName)]);
        public Task<StoredPatientDocument> MigrateLegacyAsync(string patientUserId, string fileName, string side, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteLegacyAsync(string patientUserId, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DiscardStagedAsync(string? token, string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteExpiredTemporaryFilesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private RegistrationDocuments NewDocuments() => new(
        factory.Services.GetRequiredService<IOptions<PatientDocumentStorageOptions>>().Value.RootPath,
        factory.Services.GetRequiredService<IDataProtectionProvider>());

    private sealed class CommitFault(bool commitOnServer) : DbTransactionInterceptor
    {
        public Guid? Target { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!commitOnServer && Target == eventData.TransactionId && Failures == 0)
            {
                Failures++;
                throw new IOException("Synthetic commit failure before the server commits.");
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (commitOnServer && Target == eventData.TransactionId && Failures == 0)
            {
                Failures++;
                throw new TimeoutException("Synthetic acknowledgement lost after the server commits.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureCommit(CommitFault fault) : SaveChangesInterceptor
    {
        public string? UserId { get; private set; }
        public Guid MessageId { get; private set; }
        public int MessageWrites { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var message = eventData.Context?.ChangeTracker.Entries<AccountEmail>().FirstOrDefault(e => e.State == EntityState.Added);
            if (message is not null)
            {
                UserId = message.Entity.UserId;
                MessageId = message.Entity.Id;
                MessageWrites++;
                fault.Target = eventData.Context!.Database.CurrentTransaction!.TransactionId;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RejectCommitVerification(CommitFault fault, bool unavailable) : DbCommandInterceptor
    {
        public int Failures { get; private set; }
        public System.Data.IsolationLevel? ObservedIsolation { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (fault.Failures != 0 && command.CommandText.Contains("[AccountEmails]", StringComparison.Ordinal))
            {
                ObservedIsolation = command.Transaction?.IsolationLevel;
                if (unavailable)
                {
                    Failures++;
                    throw new IOException("Synthetic commit verification unavailable.");
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Verification_insert_failure_rolls_back_signup_claims_document_and_audit()
    {
        var email = $"registration.outbox.{Guid.NewGuid():N}@fixtures.test";
        var invitationCode = HospitalRecordCodes.Generate();
        var recordCode = HospitalRecordCodes.Generate();
        var birthDate = new DateTime(1990, 3, 12);
        int invitationId, patientId, registrationCodeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var actor = await db.Users.AsNoTracking().SingleAsync(u => u.Email == PortalFactory.PrimaryEmail);
            var now = DateTime.UtcNow;
            var invitation = new StaffInvitation
            {
                Email = email, NormalizedEmail = email.ToUpperInvariant(), Role = "LabStaff",
                CodeHash = HospitalRecordCodes.Hash(HospitalRecordCodes.Normalize(invitationCode)!),
                CodeHint = invitationCode[^5..], InvitedById = actor.Id, InvitedByEmail = actor.Email!,
                CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(1),
            };
            var registration = new PatientRegistrationCode
            {
                PatientRecord = new PatientRecord { FirstName = "Synthetic", LastName = "Staff", DateOfBirth = birthDate,
                    HospitalNumber = "OUTBOX-" + Guid.NewGuid().ToString("N") },
                CodeHash = HospitalRecordCodes.Hash(HospitalRecordCodes.Normalize(recordCode)!),
                CodeHint = recordCode[^5..], IssuingPoint = RegistrationCodeIssuingPoints.Pacd,
                IssuedById = actor.Id, IssuedByEmail = actor.Email!, IssuedAtUtc = now, ExpiresAtUtc = now.AddDays(1),
            };
            db.StaffInvitations.Add(invitation);
            db.PatientRegistrationCodes.Add(registration);
            await db.SaveChangesAsync();
            invitationId = invitation.Id;
            patientId = registration.PatientRecordId;
            registrationCodeId = registration.Id;
        }

        var fault = new RejectVerificationInsert();
        var documents = NewDocuments();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(fault));
            services.RemoveAll<IPatientDocumentStorage>();
            services.AddSingleton<IPatientDocumentStorage>(documents);
        }));
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var form = SignupForms.Signup(email, recordCode, invitationCode, birthDate);
        form["Input.TempFrontPhotoToken"] = "synthetic-front";
        form["Input.TempBackPhotoToken"] = "synthetic-back";
        var response = await SignupForms.PostAsync(client, form);

        Assert.Equal(1, fault.Failures);
        Assert.NotNull(fault.UserId);
        Assert.Equal(2, documents.Created);
        Assert.Empty(documents.Files);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Id == fault.UserId || u.Email == email));
            Assert.False(await db.PatientIdDocuments.AnyAsync(d => d.PatientUserId == fault.UserId));
            Assert.False(await db.AccountEmails.AnyAsync(m => m.UserId == fault.UserId));
            Assert.False(await db.AuditLogs.AnyAsync(a => a.UserId == fault.UserId));
            Assert.Null(await db.PatientRecords.Where(p => p.Id == patientId).Select(p => p.PortalUserId).SingleAsync());
            var invitation = await db.StaffInvitations.AsNoTracking().SingleAsync(i => i.Id == invitationId);
            Assert.Null(invitation.AcceptedAtUtc);
            Assert.Null(invitation.AcceptedByUserId);
            var code = await db.PatientRegistrationCodes.AsNoTracking().SingleAsync(c => c.Id == registrationCodeId);
            Assert.Null(code.RedeemedAtUtc);
            Assert.Null(code.RedeemedByUserId);
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Synthetic verification outbox persistence failure", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Commit_faults_preserve_files_until_the_registration_outcome_is_known(bool commitOnServer, bool verificationUnavailable)
    {
        var email = $"registration.commit.{Guid.NewGuid():N}@fixtures.test";
        var invitationCode = HospitalRecordCodes.Generate();
        int invitationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;
            var invitation = new StaffInvitation
            {
                Email = email, NormalizedEmail = email.ToUpperInvariant(), Role = "LabStaff",
                CodeHash = HospitalRecordCodes.Hash(HospitalRecordCodes.Normalize(invitationCode)!), CodeHint = invitationCode[^5..],
                CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(1),
            };
            db.StaffInvitations.Add(invitation);
            await db.SaveChangesAsync();
            invitationId = invitation.Id;
        }
        var fault = new CommitFault(commitOnServer);
        var capture = new CaptureCommit(fault);
        var verification = new RejectCommitVerification(fault, verificationUnavailable);
        var documents = NewDocuments();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.AddInterceptors(fault, capture, verification);
            });
            services.RemoveAll<IPatientDocumentStorage>();
            services.AddSingleton<IPatientDocumentStorage>(documents);
        }));
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var form = SignupForms.Signup(email, invitation: invitationCode);
        form["Input.TempFrontPhotoToken"] = "synthetic-front";
        form["Input.TempBackPhotoToken"] = "synthetic-back";
        var response = await SignupForms.PostAsync(client, form);

        Assert.Equal(1, fault.Failures);
        Assert.Equal(System.Data.IsolationLevel.Serializable, verification.ObservedIsolation);
        Assert.Equal(1, capture.MessageWrites);
        Assert.NotNull(capture.UserId);
        Assert.Equal(2, documents.Created);
        Assert.Equal(commitOnServer ? 2 : 0, documents.Files.Count);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(commitOnServer ? 1 : 0, await db.Users.CountAsync(u => u.Id == capture.UserId));
            Assert.Equal(commitOnServer ? 1 : 0, await db.AccountEmails.CountAsync(m => m.Id == capture.MessageId && m.UserId == capture.UserId));
            Assert.Equal(commitOnServer ? 1 : 0, await db.AuditLogs.CountAsync(a => a.UserId == capture.UserId && a.Action == "ACCEPT_STAFF_INVITATION"));
            var invitation = await db.StaffInvitations.AsNoTracking().SingleAsync(i => i.Id == invitationId);
            Assert.Equal(commitOnServer, invitation.AcceptedAtUtc is not null);
            Assert.Equal(commitOnServer ? capture.UserId : null, invitation.AcceptedByUserId);
            var document = await db.PatientIdDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.PatientUserId == capture.UserId);
            Assert.Equal(commitOnServer, document is not null);
            if (document is not null)
            {
                Assert.True(documents.Files.ContainsKey((capture.UserId!, document.FrontPhotoFileName!)));
                Assert.True(documents.Files.ContainsKey((capture.UserId!, document.BackPhotoFileName!)));
            }
        }
        if (verificationUnavailable)
        {
            Assert.True(verification.Failures > 0);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("We could not confirm whether signup completed", await response.Content.ReadAsStringAsync());
        }
        else if (commitOnServer)
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Identity/Account/RegisterConfirmation?", response.Headers.Location?.OriginalString);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("We could not complete signup", await response.Content.ReadAsStringAsync());
        }
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Synthetic acknowledgement", html);
        Assert.DoesNotContain("Synthetic commit failure", html);
        Assert.DoesNotContain("Synthetic commit verification", html);
    }
}
