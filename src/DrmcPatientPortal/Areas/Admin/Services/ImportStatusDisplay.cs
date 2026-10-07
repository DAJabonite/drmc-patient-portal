using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed record ImportStatusDisplay(string Text, string Css)
{
    public static bool Pending(ImportStatus status) => status is ImportStatus.Queued or ImportStatus.Running
        or ImportStatus.ValidationQueued or ImportStatus.Validating or ImportStatus.ApprovalQueued or ImportStatus.Approving;
    public static bool CanCancel(ImportStatus status) => status is ImportStatus.Staged or ImportStatus.Validated
        or ImportStatus.Queued or ImportStatus.ValidationQueued or ImportStatus.ApprovalQueued or ImportStatus.Failed;

    public static ImportStatusDisplay For(ImportStatus status, bool valid = false) => status == ImportStatus.Validated
        ? new(valid ? "Validated" : "Review required", "admin-chip admin-chip-" + (valid ? "success" : "warning"))
        : new(AdminChips.Humanize(status), AdminChips.Css(status.ToString()));

    public static bool VerifiedReport(ImportBatch batch, ImportEnvelope envelope) => envelope.Report is { Valid: true } report
        && report.Rows.Count > 0 && !batch.StagingPurged && batch.ValidationVersion == ImportTemplates.ValidationVersion
        && envelope.Template == batch.Template && ImportStaging.FileHash(envelope.Content) == batch.FileHash
        && report.Hash == batch.ValidationHash && ImportStaging.ApprovalHash(envelope) == report.Hash;

    public static async Task<ImportStatusDisplay> LoadAsync(ImportBatch batch, ImportStaging staging, CancellationToken token)
    {
        var valid = false;
        if (batch.Status == ImportStatus.Validated && !batch.StagingPurged)
        {
            try { valid = VerifiedReport(batch, await staging.LoadAsync(batch.Id, token)); }
            catch (Exception error) when (error is ImportRejectedException or UnauthorizedAccessException) { }
        }
        return For(batch.Status, valid);
    }

    public static async Task<Dictionary<Guid, ImportStatusDisplay>> LoadAsync(IEnumerable<ImportBatch> batches, ImportStaging staging, CancellationToken token)
    {
        var displays = new Dictionary<Guid, ImportStatusDisplay>();
        foreach (var batch in batches) displays[batch.Id] = await LoadAsync(batch, staging, token);
        return displays;
    }
}
