using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Models;

// These records deliberately have no relationship to the shared hospital patient registry.
// Only the clinic's verified account binding conveys portal ownership.
public sealed class RedStarPatient
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string PatientCode { get; set; } = "";
    [StringLength(120)] public string CodeName { get; set; } = "";
    [Required, StringLength(200)] public string FullName { get; set; } = "";
    [StringLength(80)] public string? HospitalNumber { get; set; }
    [StringLength(40)] public string? ContactNumber { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    public string? PortalUserId { get; set; }
    public string? PatientCodeBlindIndex { get; set; }
    public string? HospitalNumberBlindIndex { get; set; }
    public string? PortalUserIdBlindIndex { get; set; }
    public bool EmailRemindersEnabled { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<RedStarEnrollment> Enrollments { get; set; } = [];
}

public enum RedStarProgram { ART, PrEP, PEP, NewbornProphylaxis, Unverified }
public enum RedStarStatus { AwaitingVerification, Active, TransferredOut, Discontinued, Completed, Deceased }
// Registry reporting categories do not introduce clinical lifecycle states.
// Keep the legacy status values so existing named and numeric filters still bind.
public enum RedStarRegistryStatusFilter { AwaitingVerification, Active, TransferredOut, Discontinued, Completed, Deceased, Living, Expired }
public enum RedStarAppointmentKind { Refill, Lab, Consultation }
public enum RedStarAppointmentState { Scheduled, PickedUp, Cancelled }
public enum RedStarRequestState { Pending, Approved, Declined }
public enum RedStarReminderChannel { InApp, Email }
public enum RedStarDeliveryState { Pending, Sending, Delivered, Failed, Cancelled }

public sealed class RedStarEnrollment
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public RedStarPatient Patient { get; set; } = null!;
    public RedStarProgram Program { get; set; }
    public RedStarStatus Status { get; set; } = RedStarStatus.AwaitingVerification;
    public DateTime? EnrolledDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? StatusDate { get; set; }
    [StringLength(200)] public string? ReceivingHub { get; set; }
    [StringLength(2000)] public string Medicines { get; set; } = "";
    [StringLength(1000)] public string? SourceStatus { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<RedStarAppointment> Appointments { get; set; } = [];
    public ICollection<RedStarDispense> Dispenses { get; set; } = [];
}

public sealed class RedStarAppointment
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public RedStarEnrollment Enrollment { get; set; } = null!;
    public RedStarAppointmentKind Kind { get; set; }
    public DateTime DueDate { get; set; }
    [StringLength(200)] public string? Label { get; set; }
    public int Revision { get; set; } = 1;
    public bool RemindersVerified { get; set; }
    public RedStarAppointmentState State { get; set; }
    public DateTimeOffset? ConfirmedUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class RedStarDispense
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public RedStarEnrollment Enrollment { get; set; } = null!;
    public int? AppointmentId { get; set; }
    public DateTime DispensedDate { get; set; }
    public int? DaysSupplied { get; set; }
    public DateTime? EstimatedSupplyEnd { get; set; }
    [StringLength(2000)] public string Medicines { get; set; } = "";
    [StringLength(450)] public string RecordedById { get; set; } = "";
}

public sealed class RedStarRescheduleRequest
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public RedStarAppointment Appointment { get; set; } = null!;
    public int AppointmentRevision { get; set; }
    public DateTime RequestedDate { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
    public RedStarRequestState State { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class RedStarReminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int AppointmentId { get; set; }
    public RedStarAppointment Appointment { get; set; } = null!;
    public int AppointmentRevision { get; set; }
    [StringLength(40)] public string Stage { get; set; } = "";
    public RedStarReminderChannel Channel { get; set; }
    [StringLength(450)] public string? PortalUserId { get; set; }
    public string? PortalUserIdBlindIndex { get; set; }
    public bool IsRead { get; set; }
    public RedStarDeliveryState State { get; set; }
    public byte[]? ProtectedPayload { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public int Attempts { get; set; }
    [StringLength(64)] public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
}

public sealed class RedStarAudit
{
    public long Id { get; set; }
    [StringLength(450)] public string ActorId { get; set; } = "";
    [StringLength(80)] public string Action { get; set; } = "";
    [StringLength(80)] public string Subject { get; set; } = "";
    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class RedStarImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [StringLength(450)] public string CreatedById { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public byte[] ProtectedSource { get; set; } = [];
    public byte[] ProtectedPreview { get; set; } = [];
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class RedStarProtectionState
{
    public int Id { get; set; }
    public bool Ready { get; set; }
    public string ProtectedCanary { get; set; } = "";
    public string BlindKeyCheck { get; set; } = "";
}
