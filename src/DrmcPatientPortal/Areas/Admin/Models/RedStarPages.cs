using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed record RedStarRegistryRow(int EnrollmentId, int PatientId, string PatientCode, string CodeName,
    RedStarProgram Program, RedStarStatus Status, DateTime? NextRefill, int PendingRequests, bool FollowUpReview);
public sealed record RedStarRegistryPage(IReadOnlyList<RedStarRegistryRow> Rows, string? Search, string Worklist,
    RedStarProgram? Program, RedStarStatus? Status, int Page, int Total, IReadOnlyDictionary<string, int> Counts);
public sealed record RedStarDetailsPage(RedStarEnrollment Enrollment, IReadOnlyList<RedStarAppointment> Appointments,
    IReadOnlyList<RedStarDispense> Dispenses, IReadOnlyList<RedStarRescheduleRequest> Requests,
    int AppointmentPage, int AppointmentTotal, int HistoryPage, int HistoryTotal, int RequestPage, int RequestTotal);
public sealed record RedStarAuditPage(IReadOnlyList<RedStarAudit> Rows, int Page, int Total);
public sealed record RedStarDeliveryRow(Guid Id, string PatientCode, DateTime DueDate, string Stage,
    RedStarReminderChannel Channel, RedStarDeliveryState State, int Attempts, DateTimeOffset CreatedUtc, DateTimeOffset? CompletedUtc);
public sealed record RedStarDeliveriesPage(IReadOnlyList<RedStarDeliveryRow> Rows, int Page, int Total);

public sealed class RedStarEnrollmentInput
{
    [StringLength(31)] public string? WorksheetLayout { get; set; }
    [Required, StringLength(80)] public string PatientCode { get; set; } = "";
    [StringLength(120)] public string? CodeName { get; set; }
    [Required, StringLength(200)] public string FullName { get; set; } = "";
    [StringLength(80)] public string? HospitalNumber { get; set; }
    [StringLength(40)] public string? ContactNumber { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    public RedStarProgram Program { get; set; } = RedStarProgram.ART;
    public RedStarStatus Status { get; set; } = RedStarStatus.AwaitingVerification;
    [DataType(DataType.Date)] public DateTime? EnrolledDate { get; set; }
    [DataType(DataType.Date)] public DateTime? StartDate { get; set; }
    [DataType(DataType.Date)] public DateTime? StatusDate { get; set; }
    [StringLength(200)] public string? ReceivingHub { get; set; }
    [StringLength(2000)] public string? Medicines { get; set; }
    [StringLength(12)] public string? PatientVersion { get; set; }
    [StringLength(12)] public string? EnrollmentVersion { get; set; }
}

public sealed class RedStarLinkInput
{
    [Required, StringLength(256), EmailAddress] public string Email { get; set; } = "";
    public bool IdentityVerified { get; set; }
    [Required, StringLength(12)] public string PatientVersion { get; set; } = "";
}
public sealed class RedStarUnlinkInput
{
    public bool IdentityVerified { get; set; }
    [Required, StringLength(12)] public string PatientVersion { get; set; } = "";
}
public sealed class RedStarScheduleInput
{
    public RedStarAppointmentKind Kind { get; set; }
    [Required, DataType(DataType.Date)] public DateTime? DueDate { get; set; }
    [StringLength(200)] public string? Label { get; set; }
    public bool RemindersVerified { get; set; }
    [Required, StringLength(12)] public string EnrollmentVersion { get; set; } = "";
    [StringLength(12)] public string? AppointmentVersion { get; set; }
}
public sealed class RedStarAppointmentCommand
{
    [Required, StringLength(12)] public string AppointmentVersion { get; set; } = "";
}
public sealed class RedStarDispenseInput
{
    [Required, StringLength(12)] public string AppointmentVersion { get; set; } = "";
    [Required, StringLength(12)] public string EnrollmentVersion { get; set; } = "";
    [Required, DataType(DataType.Date)] public DateTime? DispensedDate { get; set; }
    [Range(1, 366)] public int? DaysSupplied { get; set; }
    [Required, StringLength(2000)] public string Medicines { get; set; } = "";
    [DataType(DataType.Date)] public DateTime? NextRefillDate { get; set; }
    public bool NextDateVerified { get; set; }
}
public sealed class RedStarRequestDecision
{
    public bool Approve { get; set; }
    [Required, StringLength(12)] public string RequestVersion { get; set; } = "";
    [Required, StringLength(12)] public string AppointmentVersion { get; set; } = "";
    public int AppointmentRevision { get; set; }
    [DataType(DataType.Date)] public DateTime? ApprovedDate { get; set; }
}
