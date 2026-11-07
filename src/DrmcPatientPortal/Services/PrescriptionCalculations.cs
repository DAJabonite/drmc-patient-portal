using System.Numerics;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

public sealed record PrescriptionCalculation(decimal? Quantity, DateTime? NextRefillDate, string? Error = null)
{
    public bool Succeeded => Error is null;
}

public static class PrescriptionCalculations
{
    private const decimal Maximum = 99999999999999.9999m;
    private const int Scale = 10000;
    public static bool HasRegimen(PrescriptionInput input) => input.FrequencyMode.HasValue || input.FrequencyValue.HasValue ||
        input.DosePerIntake.HasValue || !string.IsNullOrWhiteSpace(input.DoseUnit) || input.DurationCount.HasValue || input.DurationUnit.HasValue || input.PrescribedQuantity.HasValue;
    private static bool Positive(decimal? value) => value is > 0 and <= Maximum && decimal.Round(value.Value, 4) == value;
    private static BigInteger Scaled(decimal value) => new(value * Scale);
    private static PrescriptionCalculation Invalid(string key) => new(null, null, key);

    public static PrescriptionCalculation Calculate(PrescriptionInput input, DateTime today)
    {
        if (!Enum.IsDefined(input.FulfillmentStatus) || input.FrequencyMode is null || !Enum.IsDefined(input.FrequencyMode.Value) ||
            input.DurationUnit is null || !Enum.IsDefined(input.DurationUnit.Value) || !Positive(input.DosePerIntake) ||
            string.IsNullOrWhiteSpace(input.DoseUnit) || input.DoseUnit.Trim().Length > 40 || input.DurationCount is not > 0 || input.PrescribedAt is null)
            return Invalid("Rx_InvalidRegimen");
        var prn = input.FrequencyMode == PrescriptionFrequencyMode.AsNeeded;
        if (prn ? input.FrequencyValue.HasValue : !Positive(input.FrequencyValue)) return Invalid("Rx_InvalidFrequency");
        if (input.FrequencyMode == PrescriptionFrequencyMode.TimesPerDay &&
            (decimal.Truncate(input.FrequencyValue!.Value) != input.FrequencyValue || input.FrequencyValue > int.MaxValue))
            return Invalid("Rx_InvalidFrequency");
        try
        {
            var start = input.PrescribedAt.Value.Date;
            var end = input.DurationUnit == PrescriptionDurationUnit.Months ? start.AddMonths(input.DurationCount.Value) : start.AddDays(input.DurationCount.Value);
            var days = (end - start).Days;
            BigInteger rateNumerator = 0, rateDenominator = 1;
            if (!prn)
            {
                var dose = Scaled(input.DosePerIntake!.Value);
                var frequency = Scaled(input.FrequencyValue!.Value);
                if (input.FrequencyMode == PrescriptionFrequencyMode.TimesPerDay)
                { rateNumerator = dose * frequency; rateDenominator = Scale * Scale; }
                else { rateNumerator = 24 * dose; rateDenominator = frequency; }
            }
            decimal quantity;
            if (prn)
            {
                if (!Positive(input.PrescribedQuantity)) return Invalid("Rx_InvalidQuantity");
                quantity = input.PrescribedQuantity!.Value;
            }
            else
            {
                var scaledQuantity = (rateNumerator * days * Scale + rateDenominator - 1) / rateDenominator;
                if (scaledQuantity <= 0 || scaledQuantity > Scaled(Maximum)) return Invalid("Rx_InvalidQuantity");
                quantity = (decimal)scaledQuantity / Scale;
            }
            if (input.FulfillmentStatus == PrescriptionFulfillmentStatus.Prescribed) return new(quantity, null);
            if (!Positive(input.DispensedQuantity) || input.DispensedQuantity > quantity) return Invalid("Rx_InvalidDispensedQuantity");
            if (input.ServedDate is null || input.ServedDate.Value.TimeOfDay != TimeSpan.Zero || input.ServedDate.Value.Date < start || input.ServedDate.Value.Date > today.Date)
                return Invalid("Rx_InvalidServedDate");
            if (prn) return new(quantity, null);
            var supplyDays = Scaled(input.DispensedQuantity!.Value) * rateDenominator / (Scale * rateNumerator);
            if (supplyDays > int.MaxValue) return Invalid("Rx_InvalidCalendar");
            return new(quantity, input.ServedDate.Value.Date.AddDays((int)supplyDays));
        }
        catch (ArgumentOutOfRangeException) { return Invalid("Rx_InvalidCalendar"); }
        catch (OverflowException) { return Invalid("Rx_InvalidQuantity"); }
    }
}
