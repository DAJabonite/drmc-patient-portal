namespace DrmcPatientPortal.Models;

// Static, general preparation guidance for imaging studies. It deliberately contains no fasting
// hours, fees, schedules or study-specific protocols: those come from the patient's request form
// or from DRMC Radiology. [UNVERIFIED: DRMC Radiology to confirm wording before production use.]
public sealed record RadiologyPreparationGuide(string Key, string Title, string Icon, IReadOnlyList<string> Items);

public static class RadiologyPreparation
{
    public static readonly IReadOnlyList<string> GeneralItems = new[]
    {
        "Bring your doctor's request form and a valid ID.",
        "Bring previous films, CDs or reports of earlier imaging, if you have them, so they can be compared.",
        "Arrive early so there is time to register before your study.",
        "Wear loose, comfortable clothing. You may be asked to change into a hospital gown.",
        "Remove metal objects, jewellery and hairpins before the study."
    };

    public static readonly IReadOnlyList<string> SafetyNotices = new[]
    {
        "Tell the staff before your study if you are pregnant or might be pregnant.",
        "Before an MRI, tell the staff if you have a pacemaker, an implant, surgical clips or any metal in your body.",
        "If your study uses contrast, tell the staff if you have a contrast allergy, kidney disease, or take medicine for diabetes."
    };

    public static readonly IReadOnlyList<RadiologyPreparationGuide> Guides = new[]
    {
        new RadiologyPreparationGuide("xray", "X-ray and fluoroscopy", "bi-person-bounding-box", new[]
        {
            "Most plain X-rays need no special preparation.",
            "Some fluoroscopy studies need preparation before the day. Follow the instructions on your doctor's request."
        }),
        new RadiologyPreparationGuide("ultrasound", "Ultrasound", "bi-soundwave", new[]
        {
            "Some ultrasound studies need preparation, such as not eating beforehand or coming with a full bladder.",
            "Which preparation applies depends on the body part being scanned. Check your doctor's request or ask Radiology."
        }),
        new RadiologyPreparationGuide("ct", "CT scan", "bi-circle-square", new[]
        {
            "Some CT scans use contrast. Your doctor's request or Radiology will tell you if yours does and how to prepare.",
            "Tell the staff about allergies, kidney disease and diabetes medicine before a contrast study."
        }),
        new RadiologyPreparationGuide("mri", "MRI", "bi-magnet", new[]
        {
            "Tell the staff about pacemakers, implants, surgical clips or metal fragments before you enter the scanner room.",
            "Remove all metal objects. Tell the staff if you are uncomfortable in enclosed spaces."
        }),
        new RadiologyPreparationGuide("mammography", "Mammography", "bi-gender-female", new[]
        {
            "Tell the staff if you are pregnant, might be pregnant, or are breastfeeding.",
            "Bring previous mammograms if you have them, so they can be compared."
        })
    };
}
