using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Models;

public enum SubsidyNeed { Medicines, Diagnostics, HospitalCare }
public enum BillPaymentStatus { Unconfirmed, Unpaid, PartiallyPaid, Paid }
public enum SubsidyEligibility { PendingReview, RequirementsNeeded, Eligible, Ineligible }
public enum SubsidyCoverage { Unconfirmed, Subsidised, PartiallySubsidised, SelfPay }

public class SubsidyApplication
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public SubsidyNeed Need { get; set; }
    public BillPaymentStatus ReportedPayment { get; set; }
    public bool GovernmentIdReady { get; set; }
    public bool ClinicalDocumentReady { get; set; }
    public bool CostDocumentReady { get; set; }
    public bool IndigencyDocumentReady { get; set; }

    // Authoritative outcomes are never accepted from patient form input.
    // These stay unconfirmed until an approved institutional integration supplies them.
    public SubsidyEligibility Eligibility { get; set; } = SubsidyEligibility.PendingReview;
    public SubsidyCoverage Coverage { get; set; } = SubsidyCoverage.Unconfirmed;
    public BillPaymentStatus ConfirmedPayment { get; set; } = BillPaymentStatus.Unconfirmed;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
}

public class SubsidyApplicationInput : SubsidyRequirementsInput
{
    [Required, EnumDataType(typeof(SubsidyNeed))]
    public SubsidyNeed? Need { get; set; }
    [Required, EnumDataType(typeof(BillPaymentStatus))]
    public BillPaymentStatus? ReportedPayment { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that you understand the application requires review at the Malasakit Center.")]
    public bool AcknowledgedReview { get; set; }
}

public class SubsidyRequirementsInput
{
    public bool GovernmentIdReady { get; set; }
    public bool ClinicalDocumentReady { get; set; }
    public bool CostDocumentReady { get; set; }
    public bool IndigencyDocumentReady { get; set; }
}
