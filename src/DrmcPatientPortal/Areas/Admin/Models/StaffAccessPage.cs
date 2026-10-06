namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed record StaffAccountRow(string Id, string Email, string Name, bool EmailConfirmed, bool TwoFactorEnabled, bool LockedOut,
    IReadOnlyList<string> Roles, string ConcurrencyStamp)
{
    public bool EligibleForGrant => EmailConfirmed && TwoFactorEnabled;
}

public sealed record StaffAccessPage(IReadOnlyList<StaffAccountRow> Rows, string? Search, bool AllAccounts, int Page, int Total)
{
    public int Pages => Math.Max(1, (Total + 24) / 25);
}
