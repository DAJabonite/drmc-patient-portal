using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using ExcelDataReader;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

// Imports are a separate private archive, never a batch in the general Admin import service.
public sealed class RedStarImports(IDataProtectionProvider protection, AdminWrites writes)
{
    private readonly IDataProtector source = protection.CreateProtector("DRMC.RedStar.ImportSource.v1");
    private readonly IDataProtector preview = protection.CreateProtector("DRMC.RedStar.ImportPreview.v1");

    public byte[] ReadSource(RedStarImportBatch batch)
    {
        try
        {
            if (batch.ProtectedSource.Length > RedStarRegistryParser.MaxBytes + 1024) throw new CryptographicException();
            var content = source.Unprotect(batch.ProtectedSource);
            if (content.Length > RedStarRegistryParser.MaxBytes) throw new CryptographicException();
            return content;
        }
        catch (CryptographicException) { throw new ImportRejectedException("The protected source is unavailable."); }
    }

    public RedStarImportPreview Read(RedStarImportBatch batch)
    {
        try
        {
            if (batch.ProtectedPreview.Length > 16 * 1024 * 1024) throw new CryptographicException();
            return JsonSerializer.Deserialize<RedStarImportPreview>(preview.Unprotect(batch.ProtectedPreview)) ?? throw new CryptographicException();
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        { throw new ImportRejectedException("The protected preview is unavailable. Upload the source again."); }
    }

    public async Task<(Guid Id, WriteResult Result)> StageAsync(byte[] content, string actor, CancellationToken token)
    {
        var parsed = RedStarRegistryParser.Parse(content);
        var id = Guid.NewGuid();
        var protectedSource = source.Protect(content);
        var protectedPreview = Protect(parsed);
        var result = await writes.RunAsync(async db =>
        {
            db.RedStarImportBatches.Add(new RedStarImportBatch
            {
                Id = id, CreatedById = actor, CreatedUtc = DateTimeOffset.UtcNow,
                ProtectedSource = protectedSource, ProtectedPreview = protectedPreview
            });
            await db.SaveChangesAsync(token);
            await RedStarAuditTrail.RecordAsync(db, actor, "ImportUpload", id.ToString("N"), token);
            return WriteResult.Success;
        }, token);
        return (id, result);
    }

    private byte[] Protect(RedStarImportPreview parsed)
    {
        var bytes = preview.Protect(JsonSerializer.SerializeToUtf8Bytes(parsed));
        if (bytes.Length > 16 * 1024 * 1024) throw new ImportRejectedException("The preview exceeds its protected storage limit.");
        return bytes;
    }

    public static RedStarImportChoice DefaultChoice(RedStarImportBatch batch, RedStarImportRow row) => new()
    {
        Key = row.Key, FullName = row.FullName,
        PatientCode = string.IsNullOrEmpty(row.PatientCode) ? $"REVIEW-{batch.Id:N}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Key)))[..8]}" : row.PatientCode,
        CodeName = row.CodeName, HospitalNumber = row.HospitalNumber, ContactNumber = row.ContactNumber,
        Address = row.Address, Medicines = row.Medicines, Program = row.Program, Status = row.Status,
        ReceivingHub = row.ReceivingHub, EnrolledDate = row.EnrolledDate, StartDate = row.StartDate,
        StatusDate = row.StatusDate, DispensedDate = row.DispensedDate, NextRefillDate = row.NextRefillDate
    };

    public Task<WriteResult> ReconcileAsync(Guid id, RedStarReconcileInput input, string actor, CancellationToken token) => writes.RunAsync(async db =>
    {
        var batch = await db.RedStarImportBatches.SingleOrDefaultAsync(x => x.Id == id, token);
        if (batch is null || batch.ApprovedUtc.HasValue) return WriteResult.Invalid("This batch is unavailable or already approved.");
        var version = AdminAudit.Version(input.Version);
        if (version is null || !version.SequenceEqual(batch.RowVersion)) return WriteResult.Stale;
        db.Entry(batch).Property(x => x.RowVersion).OriginalValue = version;
        var envelope = Read(batch);
        if (input.Choices.Count is 0 or > 25 || input.Choices.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != input.Choices.Count)
            return WriteResult.Invalid("Review at most 25 distinct rows at a time.");
        foreach (var choice in input.Choices)
        {
            var row = envelope.Rows.SingleOrDefault(x => x.Key == choice.Key);
            if (row is null) return WriteResult.Invalid("A source row is no longer available.");
            if (!choice.Include) { envelope.Choices.RemoveAll(x => x.Key == choice.Key); continue; }
            Normalize(choice);
            var error = Validate(choice, row);
            if (error is not null) return WriteResult.Invalid(error);
            choice.ExistingPatientVersion = choice.ExistingEnrollmentVersion = choice.ExistingScheduleVersion = null;
            choice.ExistingEnrollmentId = null;
            if (choice.ExistingPatientId.HasValue)
            {
                var patient = await db.RedStarPatients.AsNoTracking().Include(x => x.Enrollments).ThenInclude(x => x.Appointments).SingleOrDefaultAsync(x => x.Id == choice.ExistingPatientId, token);
                if (patient is null) return WriteResult.Invalid("The selected private patient record does not exist.");
                // Capture the reviewed version, never accept version tokens supplied for another record.
                choice.ExistingPatientVersion = Convert.ToBase64String(patient.RowVersion);
                var enrollment = patient.Enrollments.SingleOrDefault(x => x.Program == choice.Program);
                choice.ExistingEnrollmentId = enrollment?.Id;
                choice.ExistingEnrollmentVersion = enrollment is null ? null : Convert.ToBase64String(enrollment.RowVersion);
                choice.ExistingScheduleVersion = enrollment is null ? null : ScheduleVersion(enrollment);
            }
            envelope.Choices.RemoveAll(x => x.Key == choice.Key);
            envelope.Choices.Add(choice);
        }
        batch.ProtectedPreview = Protect(envelope);
        await db.SaveChangesAsync(token);
        await RedStarAuditTrail.RecordAsync(db, actor, "ImportReconcile", id.ToString("N"), token);
        return WriteResult.Success;
    }, token);

    private static void Normalize(RedStarImportChoice choice)
    {
        choice.PatientCode = choice.PatientCode.Trim(); choice.FullName = choice.FullName.Trim();
        choice.CodeName = choice.CodeName.Trim(); choice.Medicines = choice.Medicines.Trim();
        choice.HospitalNumber = choice.HospitalNumber?.Trim(); choice.ContactNumber = choice.ContactNumber?.Trim();
        choice.Address = choice.Address?.Trim(); choice.ReceivingHub = choice.ReceivingHub?.Trim();
        choice.EnrolledDate = choice.EnrolledDate?.Date; choice.StartDate = choice.StartDate?.Date;
        choice.StatusDate = choice.StatusDate?.Date; choice.DispensedDate = choice.DispensedDate?.Date; choice.NextRefillDate = choice.NextRefillDate?.Date;
    }

    private static string? Validate(RedStarImportChoice choice, RedStarImportRow row)
    {
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(choice);
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(choice, context, [], true) ||
            !Enum.IsDefined(choice.Program) || !Enum.IsDefined(choice.Status))
            return $"Review required fields and field lengths in {row.Sheet}, row {row.Number}.";
        if (row.Sheet.Equals("RHIVDA 2025", StringComparison.OrdinalIgnoreCase) &&
            (choice.Program != RedStarProgram.Unverified || choice.Status != RedStarStatus.AwaitingVerification))
            return "Name-only rHIVda entries must remain unverified and awaiting verification. Verify clinical enrollment separately.";
        if (choice.PatientCode.Any(char.IsControl) || choice.FullName.Any(char.IsControl)) return "Names and private codes must not contain control characters.";
        if (row.SourceStatus?.Length > 1000) return "The source status exceeds its field limit. Exclude that row and review its protected source separately.";
        if (choice.Status is RedStarStatus.TransferredOut or RedStarStatus.Discontinued or RedStarStatus.Completed or RedStarStatus.Deceased && !choice.StatusDate.HasValue)
            return "Record the effective date for transferred, discontinued, completed or deceased status.";
        if (choice.Status == RedStarStatus.TransferredOut && string.IsNullOrWhiteSpace(choice.ReceivingHub)) return "Record the receiving hub for a transfer.";
        if (choice.Program == RedStarProgram.Unverified && choice.Status == RedStarStatus.Active) return "Verify the program before creating an active clinical enrollment.";
        if (new[] { choice.EnrolledDate, choice.StartDate, choice.StatusDate, choice.DispensedDate, choice.NextRefillDate }
            .Any(x => x.HasValue && (x.Value.Year < 1900 || x.Value.Year > 2200))) return "Dates must be between 1900 and 2200.";
        return null;
    }

    private static string ScheduleVersion(RedStarEnrollment enrollment) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        enrollment.Appointments.OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion, x.State, x.DueDate, x.Revision }))));

    public Task<WriteResult> ApproveAsync(Guid id, string versionText, string actor, CancellationToken token) => writes.RunAsync(async db =>
    {
        var batch = await db.RedStarImportBatches.SingleOrDefaultAsync(x => x.Id == id, token);
        if (batch is null || batch.ApprovedUtc.HasValue) return WriteResult.Invalid("This batch is unavailable or already approved.");
        var version = AdminAudit.Version(versionText);
        if (version is null || !version.SequenceEqual(batch.RowVersion)) return WriteResult.Stale;
        db.Entry(batch).Property(x => x.RowVersion).OriginalValue = version;
        var envelope = Read(batch);
        var choices = envelope.Choices.Where(x => x.Include).ToArray();
        if (choices.Length == 0) return WriteResult.Invalid("Save at least one included row before approval.");
        var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in choices)
        {
            var row = envelope.Rows.Single(x => x.Key == choice.Key);
            var invalid = Validate(choice, row);
            if (invalid is not null) return WriteResult.Invalid(invalid);
            var target = choice.ExistingPatientId.HasValue ? $"id:{choice.ExistingPatientId}:{choice.Program}" : $"code:{choice.PatientCode}:{choice.Program}";
            if (!targetKeys.Add(target) || (!choice.ExistingPatientId.HasValue && !newCodes.Add(choice.PatientCode)))
                return WriteResult.Invalid("Multiple source rows target the same record. Include one row, or explicitly select separate existing enrollments.");
            RedStarPatient patient;
            RedStarEnrollment? enrollment = null;
            if (choice.ExistingPatientId.HasValue)
            {
                var existing = await db.RedStarPatients.Include(x => x.Enrollments).ThenInclude(x => x.Appointments)
                    .Include(x => x.Enrollments).ThenInclude(x => x.Dispenses)
                    .SingleOrDefaultAsync(x => x.Id == choice.ExistingPatientId, token);
                var expected = AdminAudit.Version(choice.ExistingPatientVersion);
                if (existing is null || expected is null || !expected.SequenceEqual(existing.RowVersion)) return WriteResult.Stale;
                patient = existing;
                enrollment = patient.Enrollments.SingleOrDefault(x => x.Program == choice.Program);
                if (enrollment?.Id != choice.ExistingEnrollmentId || (enrollment is not null &&
                    (!(AdminAudit.Version(choice.ExistingEnrollmentVersion)?.SequenceEqual(enrollment.RowVersion) ?? false) ||
                    ScheduleVersion(enrollment) != choice.ExistingScheduleVersion))) return WriteResult.Stale;
                db.Entry(patient).Property(x => x.RowVersion).OriginalValue = expected;
                if (enrollment is not null) db.Entry(enrollment).Property(x => x.RowVersion).OriginalValue = AdminAudit.Version(choice.ExistingEnrollmentVersion)!;
            }
            else { patient = new RedStarPatient(); db.RedStarPatients.Add(patient); }
            if (await db.RedStarPatients.AnyAsync(x => x.PatientCode == choice.PatientCode && x.Id != patient.Id, token))
                return WriteResult.Invalid("A private code already exists. Select that private record explicitly or exclude the duplicate row.");
            patient.PatientCode = choice.PatientCode; patient.FullName = choice.FullName;
            if (!string.IsNullOrEmpty(choice.CodeName)) patient.CodeName = choice.CodeName;
            if (!string.IsNullOrEmpty(choice.HospitalNumber)) patient.HospitalNumber = choice.HospitalNumber;
            if (!string.IsNullOrEmpty(choice.ContactNumber)) patient.ContactNumber = choice.ContactNumber;
            if (!string.IsNullOrEmpty(choice.Address)) patient.Address = choice.Address;
            // Portal ownership and email consent are never imported, even during an explicit update.
            if (enrollment is null)
            {
                enrollment = new RedStarEnrollment { Patient = patient, Program = choice.Program };
                db.RedStarEnrollments.Add(enrollment);
            }
            enrollment.Status = choice.Status;
            enrollment.SourceStatus = row.SourceStatus;
            if (choice.EnrolledDate.HasValue) enrollment.EnrolledDate = choice.EnrolledDate;
            if (choice.StartDate.HasValue) enrollment.StartDate = choice.StartDate;
            if (choice.StatusDate.HasValue) enrollment.StatusDate = choice.StatusDate;
            if (!string.IsNullOrEmpty(choice.ReceivingHub)) enrollment.ReceivingHub = choice.ReceivingHub;
            if (!string.IsNullOrEmpty(choice.Medicines)) enrollment.Medicines = choice.Medicines;
            if (choice.Status != RedStarStatus.Active && enrollment.Id != 0)
                await InvalidateSchedulesAsync(db, enrollment, null, true, token);
            // A spreadsheet date provides no evidence of days supplied or supply end.
            if (choice.DispensedDate.HasValue && !enrollment.Dispenses.Any(x => x.DispensedDate == choice.DispensedDate.Value))
                db.RedStarDispenses.Add(new RedStarDispense { Enrollment = enrollment, DispensedDate = choice.DispensedDate.Value,
                    Medicines = choice.Medicines, RecordedById = actor });
            if (choice.NextRefillDate.HasValue)
            {
                var same = enrollment.Appointments.SingleOrDefault(x => x.Kind == RedStarAppointmentKind.Refill &&
                    x.DueDate == choice.NextRefillDate.Value && x.State == RedStarAppointmentState.Scheduled);
                if (enrollment.Id != 0) await InvalidateSchedulesAsync(db, enrollment, RedStarAppointmentKind.Refill, false, token);
                foreach (var old in enrollment.Appointments.Where(x => x.Kind == RedStarAppointmentKind.Refill && x.State == RedStarAppointmentState.Scheduled && x != same))
                    old.State = RedStarAppointmentState.Cancelled;
                if (same is null)
                    db.RedStarAppointments.Add(new RedStarAppointment { Enrollment = enrollment, Kind = RedStarAppointmentKind.Refill,
                        DueDate = choice.NextRefillDate.Value, State = choice.Status == RedStarStatus.Active ? RedStarAppointmentState.Scheduled : RedStarAppointmentState.Cancelled, RemindersVerified = false });
            }
        }
        batch.ApprovedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await RedStarAuditTrail.RecordAsync(db, actor, "ImportApprove", id.ToString("N"), token);
        return WriteResult.Success;
    }, token);

    private static async Task InvalidateSchedulesAsync(ApplicationDbContext db, RedStarEnrollment enrollment,
        RedStarAppointmentKind? kind, bool close, CancellationToken token)
    {
        var query = db.RedStarAppointments.Where(x => x.EnrollmentId == enrollment.Id && (!kind.HasValue || x.Kind == kind.Value));
        await RedStarAuditTrail.CancelRemindersAsync(db, query, token);
        var ids = enrollment.Appointments.Where(x => !kind.HasValue || x.Kind == kind.Value).Select(x => x.Id).ToArray();
        await db.RedStarRescheduleRequests.Where(x => ids.Contains(x.AppointmentId) && x.State == RedStarRequestState.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.State, RedStarRequestState.Declined), token);
        foreach (var appointment in enrollment.Appointments.Where(x => (!kind.HasValue || x.Kind == kind.Value) && x.State == RedStarAppointmentState.Scheduled))
        {
            appointment.RemindersVerified = false; appointment.Revision++; appointment.ConfirmedUtc = null;
            if (close) appointment.State = RedStarAppointmentState.Cancelled;
        }
    }
}

public static class RedStarRegistryParser
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxRows = 5000;
    private static readonly HashSet<string> SheetNames = new(StringComparer.OrdinalIgnoreCase)
    { "LIVING", "PrEP", "PEP", "NB PROPHYLAXIS", "TRANS OUT", "EXPIRED", "REACTIVE WHEN TESTED, NONREACTI", "RHIVDA 2025" };

    public static RedStarImportPreview Parse(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new ImportRejectedException("Upload a non-empty .xlsx workbook of at most 10 MB.");
        try
        {
            var formulaCells = CheckWorkbook(bytes);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var stream = new MemoryStream(bytes);
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
            var result = new RedStarImportPreview();
            var deferred = new HashSet<string>(StringComparer.Ordinal);
            var sheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var physicalRows = 0;
            do
            {
                var sheet = reader.Name.Trim();
                if (!SheetNames.Contains(sheet) || !sheets.Add(sheet)) throw new ImportRejectedException("Use the recognized Red Star registry sheet names, once per workbook.");
                if (!reader.Read()) continue;
                if (reader.FieldCount > 128) throw new ImportRejectedException("A registry sheet exceeds 128 columns.");
                var header = Enumerable.Range(0, reader.FieldCount).Select(i => Text(reader.GetValue(i))).ToArray();
                var fields = header.Select(Field).ToArray();
                var headerless = sheet.Equals("RHIVDA 2025", StringComparison.OrdinalIgnoreCase) && !fields.Contains("name");
                if (!headerless && !fields.Contains("name") && !fields.Contains("code"))
                    throw new ImportRejectedException("Each registry sheet needs a patient name or patient code header in its first row.");
                if (headerless) { fields = new string?[reader.FieldCount]; fields[0] = "name"; }
                else
                {
                    // The supplied registry stores regimen components in adjacent unlabeled cells.
                    var medicine = Array.IndexOf(fields, "medicines");
                    if (medicine >= 0) for (var c = medicine + 1; c < fields.Length && c <= medicine + 2 && string.IsNullOrWhiteSpace(header[c]); c++) fields[c] = "medicines";
                    for (var c = 0; c < fields.Length; c++) if (fields[c] is null && !string.IsNullOrWhiteSpace(header[c])) deferred.Add(sheet + ": " + header[c]);
                }
                var number = headerless ? 0 : 1;
                do
                {
                    if (!headerless && number == 1) { number++; if (!reader.Read()) break; }
                    else number++;
                    if (++physicalRows > 100000) throw new ImportRejectedException("The workbook exceeds its row safety limit.");
                    var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i)).ToArray();
                    if (values.All(x => string.IsNullOrWhiteSpace(Text(x)))) { result.IgnoredRows++; continue; }
                    var name = Value(values, fields, "name"); var code = Value(values, fields, "code");
                    if (IsNote(name) || IsNote(code) || (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(code))) { result.IgnoredRows++; continue; }
                    if (result.Rows.Count == MaxRows) throw new ImportRejectedException("Import at most 5,000 patient rows per workbook.");
                    for (var c = 0; c < fields.Length; c++)
                        if (fields[c] is not null && formulaCells.TryGetValue(sheet, out var formulas) && formulas.Contains(Column(c) + number))
                            throw new ImportRejectedException("Imported patient fields must contain values, not formulas. Export core fields as values before uploading.");
                    var row = new RedStarImportRow { Key = sheet + ":" + number, Sheet = sheet, Number = number,
                        FullName = name, PatientCode = code, CodeName = Value(values, fields, "codename"),
                        HospitalNumber = Optional(Value(values, fields, "hospital")), ContactNumber = Optional(Value(values, fields, "contact")),
                        Address = Optional(Value(values, fields, "address")), ReceivingHub = Optional(Value(values, fields, "hub")),
                        Medicines = string.Join(" / ", fields.Select((f, i) => f == "medicines" ? Text(values[i]) : "").Where(x => !string.IsNullOrWhiteSpace(x))),
                        SourceStatus = Optional(Value(values, fields, "status")), Program = Program(sheet), Status = Status(sheet) };
                    var explicitProgram = Value(values, fields, "program");
                    if (explicitProgram.Length != 0)
                    {
                        if (Enum.TryParse<RedStarProgram>(explicitProgram, true, out var program) && Enum.IsDefined(program)) row.Program = program;
                        else { row.Program = RedStarProgram.Unverified; row.Warnings.Add("Unknown program; verify before clinical enrollment."); }
                    }
                    row.EnrolledDate = Date(values, fields, "enrolled", row);
                    row.StartDate = Date(values, fields, "start", row);
                    row.StatusDate = Date(values, fields, "statusdate", row);
                    row.DispensedDate = Date(values, fields, "dispensed", row);
                    row.NextRefillDate = Date(values, fields, "next", row);
                    ApplyStatus(row);
                    row.ContactNumber = NormalizePhone(row.ContactNumber, row.Warnings);
                    if (row.PatientCode.Length == 0) row.Warnings.Add("Missing clinic code: assign a private review code or explicitly select an existing record.");
                    if (row.FullName.Length == 0) row.Warnings.Add("Missing patient name: reconcile it before including this row.");
                    if (row.Program == RedStarProgram.Unverified || headerless)
                    { row.Program = RedStarProgram.Unverified; row.Status = RedStarStatus.AwaitingVerification; row.Warnings.Add("This row does not establish a test result or treatment enrollment."); }
                    if (!row.NextRefillDate.HasValue) row.Warnings.Add("No next refill date is recorded; no schedule will be created.");
                    if (row.NextRefillDate.HasValue) row.Warnings.Add("Imported schedules remain inactive for reminders until clinic staff verify them.");
                    result.Rows.Add(row);
                } while (reader.Read());
            } while (reader.NextResult());
            if (result.Rows.Count == 0) throw new ImportRejectedException("The workbook contains no patient rows to review.");
            foreach (var group in result.Rows.Where(x => x.PatientCode.Length != 0).GroupBy(x => x.PatientCode, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                foreach (var row in group) row.Warnings.Add("This clinic code appears more than once in the workbook. Reconcile duplicates explicitly.");
            result.DeferredColumns = deferred.OrderBy(x => x, StringComparer.Ordinal).ToList();
            return result;
        }
        catch (Exception error) when (error is IOException or XmlException or ArgumentException or NotSupportedException or OverflowException or ExcelDataReader.Exceptions.ExcelReaderException)
        { throw new ImportRejectedException("The workbook is malformed or uses an unsupported format."); }
    }

    private static string Text(object? value) => value is DateTime date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Trim();
    private static string Value(object?[] values, string?[] fields, string key) => Array.IndexOf(fields, key) is var index && index >= 0 ? Text(values[index]) : "";
    private static string? Optional(string value) => value.Length == 0 ? null : value;
    private static bool IsNote(string value) => value.Length > 250 || Regex.IsMatch(value, @"^(note|reminder|instructions?|lost to follow|ltfu)\b", RegexOptions.IgnoreCase) ||
        value.Contains("7 days before", StringComparison.OrdinalIgnoreCase) || value.Contains("monthly reminder", StringComparison.OrdinalIgnoreCase);
    private static string? Field(string header)
    {
        var key = Regex.Replace(header.ToUpperInvariant(), "[^A-Z0-9]", "");
        return key switch
        {
            "NAME" or "PATIENTNAME" or "FULLNAME" => "name", "PATIENTCODE" => "code", "CODENAME" => "codename",
            "HRN" or "HOSPITALNO" or "HOSPITALNUMBER" => "hospital", "LASTCONTACTNOPROVIDED" or "CONTACTNUMBER" => "contact",
            "ADDRESS" or "CURRENTADDRESS" or "LASTADDRESSRESIDED" => "address", "NAMEOFHUB" or "RECEIVINGHUB" => "hub",
            "REGIMEN" or "ARVREGIMEN" or "PREPREGIMEN" or "MEDICINES" => "medicines",
            "STATUS" or "TREATMENTSTATUS" => "status", "PROGRAM" => "program",
            "REFILLDATE" or "ARVREFILLDATE" or "DISPENSEDDATE" => "dispensed",
            "NEXTREFILLDATE" or "PREPNEXTREFILLDATE" => "next",
            "ENROLMENTBASELINEDATE" or "DATEENROLLEDTORSHACT" or "DATEENROLLEDREDSTAR" or "DATEENROLLEDTOHACT" or "ENROLLEDDATE" => "enrolled",
            "ARTSTARTDATEREDSTAR" or "ARTSTARTDATE" or "PREPSTARTDATEREDSTAR" or "STARTDATE" => "start",
            "DATEEXPIRED" or "TRANSOUTDATE" or "STATUSDATE" => "statusdate", _ => null
        };
    }

    private static DateTime? Date(object?[] values, string?[] fields, string key, RedStarImportRow row)
    {
        var index = Array.IndexOf(fields, key);
        if (index < 0 || string.IsNullOrWhiteSpace(Text(values[index]))) return null;
        if (values[index] is DateTime date && date.Year is >= 1900 and <= 2200) return date.Date;
        if (values[index] is double serial && serial is > 1 and < 110000)
        { var converted = DateTime.FromOADate(serial); if (converted.Year <= 2200) return converted.Date; }
        var text = Text(values[index]);
        if (DateTime.TryParseExact(text, ["yyyy-MM-dd", "M/d/yyyy", "M/d/yy", "MM/dd/yyyy", "d-MMM-yyyy", "MMMM d, yyyy", "MMM d, yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date) && date.Year is >= 1900 and <= 2200) return date.Date;
        row.Warnings.Add($"The {key} date could not be read; enter a verified date or deliberately leave it empty. The original remains in the protected archive.");
        return null;
    }

    private static RedStarProgram Program(string sheet) => sheet.ToUpperInvariant() switch
    { "PREP" => RedStarProgram.PrEP, "PEP" => RedStarProgram.PEP, "NB PROPHYLAXIS" => RedStarProgram.NewbornProphylaxis,
        "RHIVDA 2025" or "REACTIVE WHEN TESTED, NONREACTI" => RedStarProgram.Unverified, _ => RedStarProgram.ART };
    private static RedStarStatus Status(string sheet) => sheet.ToUpperInvariant() switch
    { "TRANS OUT" => RedStarStatus.TransferredOut, "EXPIRED" => RedStarStatus.Deceased,
        "RHIVDA 2025" or "REACTIVE WHEN TESTED, NONREACTI" => RedStarStatus.AwaitingVerification, _ => RedStarStatus.Active };
    private static void ApplyStatus(RedStarImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.SourceStatus)) return;
        var normalized = row.SourceStatus.ToUpperInvariant();
        if (normalized.Contains("EXPIRED") || normalized.Contains("DECEASED")) row.Status = RedStarStatus.Deceased;
        else if (normalized.Contains("TRANS OUT") || normalized.Contains("TRANSOUT") || normalized.Contains("TRANSFERRED")) row.Status = RedStarStatus.TransferredOut;
        else if (normalized.Contains("D/C") || normalized.Contains("DISCONTINUED")) row.Status = RedStarStatus.Discontinued;
        else if (normalized.Contains("COMPLETED")) row.Status = RedStarStatus.Completed;
        else if (normalized is "ACTIVE" or "ON ART" or "ON PREP" or "ON PEP") row.Status = RedStarStatus.Active;
        else if (Enum.TryParse<RedStarStatus>(row.SourceStatus, true, out var status) && Enum.IsDefined(status)) row.Status = status;
        else { row.Status = RedStarStatus.AwaitingVerification; row.Warnings.Add("Ambiguous source status: verify the reconciled status before approval."); }
        var date = Regex.Match(normalized, @"\b\d{1,2}/\d{1,2}/\d{4}\b");
        if (date.Success && !row.StatusDate.HasValue && DateTime.TryParseExact(date.Value, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effective))
        { row.StatusDate = effective.Date; row.Warnings.Add("A status date was extracted as month/day/year. Confirm it during reconciliation."); }
    }

    public static string? NormalizePhone(string? phone, List<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = Regex.Replace(phone, @"[^0-9]", "");
        if (Regex.IsMatch(digits, @"^09\d{9}$")) return "+63" + digits[1..];
        if (Regex.IsMatch(digits, @"^9\d{9}$")) return "+63" + digits;
        if (Regex.IsMatch(digits, @"^639\d{9}$")) return "+" + digits;
        warnings?.Add("Contact number needs staff verification; the source value is retained. No SMS is sent.");
        return phone.Trim();
    }

    private static string Column(int zeroBased)
    {
        var result = "";
        for (var number = zeroBased + 1; number > 0; number = (number - 1) / 26) result = (char)('A' + (number - 1) % 26) + result;
        return result;
    }

    private static Dictionary<string, HashSet<string>> CheckWorkbook(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K' || bytes[2] != 3 || bytes[3] != 4)
            throw new ImportRejectedException("Only unencrypted OpenXML .xlsx workbooks are accepted.");
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 256 || zip.Entries.Sum(x => x.Length) > 60L * 1024 * 1024)
            throw new ImportRejectedException("The workbook exceeds archive safety limits.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var formulas = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (!names.Add(entry.FullName) || entry.FullName.Contains("..", StringComparison.Ordinal) || entry.FullName.Contains('\\') ||
                entry.FullName.StartsWith('/') || entry.Length > 20L * 1024 * 1024 ||
                (entry.Length > 1024 * 1024 && entry.Length > Math.Max(1, entry.CompressedLength) * 250))
                throw new ImportRejectedException("The workbook contains unsafe archive entries.");
            if (entry.FullName.Contains("externalLink", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("vba", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                throw new ImportRejectedException("Macros and external links are not accepted.");
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
            using var part = entry.Open();
            using var xml = XmlReader.Create(part, SafeXml());
            var cell = "";
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element) continue;
                if (xml.LocalName == "c") cell = xml.GetAttribute("r") ?? "";
                if (xml.LocalName == "f")
                {
                    if (!formulas.TryGetValue(entry.FullName, out var refs)) formulas[entry.FullName] = refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    refs.Add(cell);
                }
                if (xml.LocalName == "definedName" || xml.LocalName.Contains("Formula", StringComparison.OrdinalIgnoreCase) ||
                    xml.GetAttribute("TargetMode")?.Equals("External", StringComparison.OrdinalIgnoreCase) == true ||
                    xml.GetAttribute("ContentType")?.Contains("macro", StringComparison.OrdinalIgnoreCase) == true)
                    throw new ImportRejectedException("Defined formulas, macros and external links are not accepted.");
            }
        }
        XDocument Load(string name)
        {
            using var part = (zip.GetEntry(name) ?? throw new ImportRejectedException("The workbook manifest is missing.")).Open();
            using var xml = XmlReader.Create(part, SafeXml()); return XDocument.Load(xml);
        }
        var relationship = (XNamespace)"http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var rels = Load("xl/_rels/workbook.xml.rels").Descendants().Where(x => x.Name.LocalName == "Relationship")
            .ToDictionary(x => (string)x.Attribute("Id")!, x => (string)x.Attribute("Target")!, StringComparer.Ordinal);
        var byName = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in Load("xl/workbook.xml").Descendants().Where(x => x.Name.LocalName == "sheet"))
        {
            var id = (string?)sheet.Attribute(relationship + "id");
            if (id is null || !rels.TryGetValue(id, out var target)) throw new ImportRejectedException("The workbook sheet manifest is invalid.");
            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            if (formulas.TryGetValue(path, out var refs)) byName[(string)sheet.Attribute("name")!] = refs;
        }
        return byName;
    }
    private static XmlReaderSettings SafeXml() => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20L * 1024 * 1024 };

    public static byte[] Export(IReadOnlyList<RedStarEnrollment> enrollments)
    {
        var groups = enrollments.ToLookup(RedStarRegistryLayout.SheetFor, StringComparer.Ordinal);
        var sheets = RedStarRegistryLayout.Sheets.Select(sheet =>
        {
            var columns = RedStarRegistryLayout.ExportColumns(sheet);
            var rows = groups[sheet.Name].Select(enrollment =>
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["FullName"] = enrollment.Patient.FullName, ["Address"] = enrollment.Patient.Address ?? "",
                    ["HospitalNumber"] = enrollment.Patient.HospitalNumber ?? "", ["PatientCode"] = enrollment.Patient.PatientCode,
                    ["CodeName"] = enrollment.Patient.CodeName, ["ContactNumber"] = enrollment.Patient.ContactNumber ?? "",
                    ["Program"] = enrollment.Program.ToString(), ["Status"] = enrollment.Status.ToString(),
                    ["EnrolledDate"] = FormatDate(enrollment.EnrolledDate), ["StartDate"] = FormatDate(enrollment.StartDate),
                    ["StatusDate"] = FormatDate(enrollment.StatusDate), ["ReceivingHub"] = enrollment.ReceivingHub ?? "",
                    ["Medicines"] = enrollment.Medicines,
                    ["DispensedDate"] = FormatDate(enrollment.Dispenses.OrderByDescending(dispense => dispense.DispensedDate)
                        .Select(dispense => (DateTime?)dispense.DispensedDate).FirstOrDefault()),
                    ["NextRefillDate"] = FormatDate(enrollment.Appointments.Where(appointment => appointment.Kind == RedStarAppointmentKind.Refill &&
                        appointment.State == RedStarAppointmentState.Scheduled).OrderBy(appointment => appointment.DueDate)
                        .Select(appointment => (DateTime?)appointment.DueDate).FirstOrDefault())
                };
                return columns.Select(column => column.Field.Length == 0 ? "" : values[column.Field]).ToArray();
            }).Prepend(columns.Select(column => column.Header).ToArray()).ToArray();
            return (sheet.Name, rows);
        }).ToList();
        return Workbook(sheets);
    }
    private static string FormatDate(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    // All cells are literal inline strings. Even '=...' and '+...' are never formulas.
    public static byte[] Workbook(IReadOnlyList<(string Name, string[][] Rows)> sheets)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            const string spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string relationship = "http://schemas.openxmlformats.org/package/2006/relationships";
            void Part(string path, Action<XmlWriter> write)
            { using var target = zip.CreateEntry(path).Open(); using var xml = XmlWriter.Create(target, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }); write(xml); }
            Part("[Content_Types].xml", xml =>
            {
                xml.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                void Type(string element, string key, string value, string contentType) { xml.WriteStartElement(element); xml.WriteAttributeString(key, value); xml.WriteAttributeString("ContentType", contentType); xml.WriteEndElement(); }
                Type("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml"); Type("Default", "Extension", "xml", "application/xml");
                Type("Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                for (var i = 1; i <= sheets.Count; i++) Type("Override", "PartName", "/xl/worksheets/sheet" + i + ".xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"); xml.WriteEndElement();
            });
            Part("_rels/.rels", xml => { xml.WriteStartElement("Relationships", relationship); xml.WriteStartElement("Relationship"); xml.WriteAttributeString("Id", "rId1"); xml.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"); xml.WriteAttributeString("Target", "xl/workbook.xml"); xml.WriteEndElement(); xml.WriteEndElement(); });
            Part("xl/workbook.xml", xml =>
            {
                xml.WriteStartElement("workbook", spreadsheet); xml.WriteStartElement("sheets");
                for (var i = 0; i < sheets.Count; i++) { xml.WriteStartElement("sheet"); xml.WriteAttributeString("name", sheets[i].Name); xml.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture)); xml.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId" + (i + 1)); xml.WriteEndElement(); }
                xml.WriteEndElement(); xml.WriteEndElement();
            });
            Part("xl/_rels/workbook.xml.rels", xml =>
            {
                xml.WriteStartElement("Relationships", relationship);
                for (var i = 1; i <= sheets.Count; i++) { xml.WriteStartElement("Relationship"); xml.WriteAttributeString("Id", "rId" + i); xml.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"); xml.WriteAttributeString("Target", "worksheets/sheet" + i + ".xml"); xml.WriteEndElement(); } xml.WriteEndElement();
            });
            for (var i = 0; i < sheets.Count; i++)
                Part("xl/worksheets/sheet" + (i + 1) + ".xml", xml =>
                {
                    xml.WriteStartElement("worksheet", spreadsheet); xml.WriteStartElement("sheetData"); var number = 1;
                    foreach (var row in sheets[i].Rows)
                    {
                        xml.WriteStartElement("row"); xml.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
                        for (var col = 0; col < row.Length; col++) { xml.WriteStartElement("c"); xml.WriteAttributeString("r", Column(col) + number); xml.WriteAttributeString("t", "inlineStr"); xml.WriteStartElement("is"); xml.WriteStartElement("t"); xml.WriteAttributeString("xml", "space", null, "preserve"); xml.WriteString(row[col]); xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement(); } xml.WriteEndElement(); number++;
                    }
                    xml.WriteEndElement(); xml.WriteEndElement();
                });
        }
        return stream.ToArray();
    }
}
