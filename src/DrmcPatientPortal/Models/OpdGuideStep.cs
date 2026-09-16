namespace DrmcPatientPortal.Models;

// Static reference data for DRMC's outpatient (OPD) facility wayfinding sequences.
// Facility names, step titles, and step instructions are clinical reference data and
// stay verbatim in English; only the surrounding page chrome is localized. Sequences and
// the verification status of every location line are logged in docs/opd-guide-content-sources.md.
public record OpdGuideStep(
    int StepNumber,
    string Title,
    string? Location = null,
    string? RequirementNote = null,
    string? Detail = null,
    IReadOnlyList<string>? Branches = null,
    bool IsConditional = false,
    IReadOnlyList<string>? Destinations = null
);

public record OpdFacilityFlow(
    string Key,
    string Name,
    string ShortName,
    OpdGuideStep? PreRegistration,
    IReadOnlyList<OpdGuideStep> Steps,
    string? VerificationNote = null
);

public static class OpdGuideFlows
{
    public static readonly IReadOnlyList<OpdFacilityFlow> All = new List<OpdFacilityFlow>
    {
        new(
            "main",
            "Main OPD",
            "Main OPD",
            new OpdGuideStep(
                0,
                "Pre-registration — PACD (Public Assistance and Complaints Desk)",
                "Main Entrance",
                null,
                "Ask for information and get the form to fill up. Staff check whether you have an appointment.",
                new List<string>
                {
                    "With an appointment — you are sent directly to the specified clinic.",
                    "Without an appointment — you are given a number to start triage."
                }
            ),
            new List<OpdGuideStep>
            {
                new(
                    1,
                    "Triage Nurse",
                    null,
                    null,
                    "Present your queue number and answer the nurse's initial screening questions."
                ),
                new(
                    2,
                    "Vital Signs",
                    null,
                    null,
                    "Staff record your basic vital signs before screening."
                ),
                new(
                    3,
                    "Screening / Verification",
                    null,
                    "Have your PhilHealth Member Data Record (MDR) ready, if available",
                    "Staff check for an existing record, follow-up status, and PhilHealth.",
                    new List<string>
                    {
                        "With PhilHealth — proceed to the PCU for member verification.",
                        "Without PhilHealth — you are directed to the Social Worker for registration."
                    }
                ),
                new(
                    4,
                    "Registration",
                    null,
                    "Government-issued ID; for infants or children, a photocopy of the birth certificate",
                    "New patients are registered; existing patients have their records pulled."
                ),
                new(
                    5,
                    "Cashier",
                    null,
                    "Present a qualifying ID at the cashier if you are eligible for the ₱0 rate",
                    "Newly registered patients with no existing record pay ₱150. Senior citizens, PWDs, and other eligible patients pay ₱0 upon presentation of a qualifying ID."
                ),
                new(
                    6,
                    "Clinic",
                    null,
                    null,
                    "You are directed to your intended clinic."
                )
            }
        ),
        new(
            "bucas",
            "BUCAS OPD",
            "BUCAS OPD",
            null,
            new List<OpdGuideStep>
            {
                new(
                    1,
                    "Queue Number",
                    null,
                    null,
                    "On arrival, get a queueing number from the guard."
                ),
                new(
                    2,
                    "Vital Signs",
                    "VS (Vital Signs) Area",
                    null,
                    "Wait at the VS area for the initial assessment."
                ),
                new(
                    3,
                    "PhilHealth Verification",
                    null,
                    "Have your PhilHealth Member Data Record (MDR) ready, if available",
                    "Proceed to PhilHealth verification."
                ),
                new(
                    4,
                    "Registration",
                    null,
                    null,
                    "After verification, proceed to patient registration."
                ),
                new(
                    5,
                    "Nurse Triage",
                    null,
                    null,
                    "The triage nurse evaluates your condition and determines the appropriate consultation process."
                ),
                new(
                    6,
                    "Wait for Consultation",
                    null,
                    null,
                    "Monitor the queueing screen for your queue number to be called."
                ),
                new(
                    7,
                    "Doctor's Consultation",
                    null,
                    null,
                    "Once called, proceed to the designated OPD consultation cubicle."
                ),
                new(
                    8,
                    "Post-Consultation Instructions",
                    null,
                    null,
                    "You are told where to go next based on the doctor's orders.",
                    Destinations: new List<string>
                    {
                        "Pharmacy",
                        "Laboratory",
                        "X-ray Department",
                        "Other required diagnostic or treatment services"
                    }
                ),
                new(
                    9,
                    "Follow-Up",
                    null,
                    null,
                    "If a follow-up consultation is required, first complete the requested laboratory tests, X-rays, or other examinations, then return for follow-up per staff instructions.",
                    null,
                    true
                )
            }
        ),
        new(
            "ccm",
            "Cancer Center for Mindanao OPD (CCM OPD)",
            "CCM OPD",
            null,
            new List<OpdGuideStep>
            {
                new(1, "Queue", null, null, "Follow staff instructions to obtain or wait for your queue number."),
                new(2, "Registration", null, null, "Provide the information requested so staff can create or retrieve your patient record."),
                new(3, "Vital Signs", null, null, "Staff record your basic vital signs before triage."),
                new(4, "Triage", null, null, "Briefly explain your concern so staff can direct you to the appropriate clinic."),
                new(5, "Clinic", null, null, "Wait for your name or queue number, then proceed when called.")
            },
            "For verification: this sequence is provisional and pending confirmation from DRMC."
        ),
        new(
            "acc",
            "Ambulatory Care Center OPD (ACC OPD)",
            "ACC OPD",
            null,
            new List<OpdGuideStep>
            {
                new(
                    1,
                    "Queue by Service",
                    null,
                    null,
                    "Queue is organized by service.",
                    Destinations: new List<string>
                    {
                        "Day Surgery",
                        "Digestive Endoscopy",
                        "Ultrasound & 2D Echo",
                        "Consultation"
                    }
                ),
                new(2, "Registration", null, null, "Provide the information requested so staff can create or retrieve your patient record."),
                new(3, "Vital Signs", null, null, "Staff record your basic vital signs before the nurse-station check."),
                new(4, "Nurse Station", null, null, "Service type is verified."),
                new(5, "Clinic", null, null, "Wait for your name or queue number, then proceed when called.")
            }
        )
    };
}
