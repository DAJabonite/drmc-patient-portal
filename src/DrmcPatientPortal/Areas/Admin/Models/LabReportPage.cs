namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed record LabReportPage(int Id, string TestName, string AccessionNumber, bool HasReport,
    long? Size, DateTime? UploadedAtUtc, string RowVersion);
