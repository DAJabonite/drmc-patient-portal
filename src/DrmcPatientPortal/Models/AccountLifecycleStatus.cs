namespace DrmcPatientPortal.Models;

public enum AccountLifecycleStatus
{
    PendingVerification = 0,
    Active = 1,
    CorrectionRequired = 2,
    Rejected = 3,
    Suspended = 4
}
