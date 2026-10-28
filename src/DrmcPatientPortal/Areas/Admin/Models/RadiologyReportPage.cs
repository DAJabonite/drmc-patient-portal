using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed record RadiologyReportPage(int Id, string StudyName, string AccessionNumber, bool HasReport,
    long? Size, DateTime? UploadedAtUtc, string RowVersion, DateTime ReleaseTime, RadiologyStatus Status);
