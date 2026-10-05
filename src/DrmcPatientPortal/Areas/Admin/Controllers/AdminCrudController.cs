using System.Globalization;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public abstract class AdminCrudController<TEntity, TInput>(ApplicationDbContext db, AdminWrites writes,
    AdminEntity<TEntity, TInput> editor) : Controller where TEntity : class, new() where TInput : class, IAdminInput, new()
{
    protected ApplicationDbContext Database => db;
    protected AdminWrites Writes => writes;
    protected AdminEntity<TEntity, TInput> Editor => editor;
    private const string Shared = "/Areas/Admin/Views/Shared/";

    [HttpGet]
    public virtual async Task<IActionResult> Index(string? search, int page = 1, int? contextId = null, CancellationToken cancellationToken = default)
    {
        if (editor.RequiresListContext && contextId is null) return await ContextSelection(search, page, "Index", cancellationToken);
        OwnershipContext? context = null;
        if (editor.NeedsContext && contextId.HasValue)
        {
            context = await editor.CreateContextAsync(db, contextId.Value, cancellationToken);
            if (context is null) return NotFound();
            await AdminAudit.ViewAsync(db, User, editor.Name, [context.Patient.Id], "Index context", cancellationToken);
        }
        search = search?.Trim();
        if (search?.Length > 450) { ModelState.AddModelError(nameof(search), "Search must be at most 450 characters."); search = null; }
        var query = editor.Query(db, contextId);
        if (!string.IsNullOrEmpty(search)) query = editor.Search(query, search);
        var total = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var entities = await query.OrderByDescending(e => EF.Property<int>(e, "Id")).Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken);
        await ReadAudit(entities, "Index", cancellationToken);
        return View(Shared + "Index.cshtml", new AdminList(editor.Title, entities.Select(e => new AdminRow(
            AdminEntity<TEntity, TInput>.Id(db, e), editor.Label(db, e), editor.Summary(e), editor.PatientId(e))).ToArray(), search, page, total, contextId, context));
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var entity = await Find(db, id, cancellationToken);
        if (entity is null) return NotFound();
        await ReadAudit([entity], id.ToString(CultureInfo.InvariantCulture), cancellationToken);
        return View(Shared + "Details.cshtml", await DetailsModel(entity, cancellationToken));
    }

    [HttpGet]
    public virtual async Task<IActionResult> Create([FromRoute] int id = 0, string? search = null, int page = 1, CancellationToken cancellationToken = default)
    {
        if (editor.NeedsContext && id == 0)
            return await ContextSelection(search, page, "Create", cancellationToken);
        var context = await editor.CreateContextAsync(db, id, cancellationToken);
        if (editor.NeedsContext && context is null) return NotFound();
        if (context is not null) await AdminAudit.ViewAsync(db, User, editor.Name, [context.Patient.Id], "Create", cancellationToken);
        return await Form("Create", new TInput(), null, context, cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Create(TInput input, [FromRoute] int id = 0, CancellationToken cancellationToken = default)
    {
        RejectOwnershipFields();
        var context = await editor.CreateContextAsync(db, id, cancellationToken);
        if (editor.NeedsContext && context is null) return NotFound();
        if (!ModelState.IsValid) return await Form("Create", input, null, context, cancellationToken);
        var result = await writes.RunAsync(async database =>
        {
            var freshContext = await editor.CreateContextAsync(database, id, cancellationToken);
            if (editor.NeedsContext && freshContext is null) return WriteResult.Invalid("The patient or parent record no longer exists.");
            var entity = new TEntity();
            var validation = await editor.ApplyAsync(database, input, entity, freshContext, true, cancellationToken);
            if (!validation.Succeeded) return validation;
            database.Set<TEntity>().Add(entity); await database.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(database, User, "Create", editor.Name, AdminEntity<TEntity, TInput>.Id(database, entity).ToString(CultureInfo.InvariantCulture),
                editor.PatientId(entity), after: AdminAudit.Values(database, entity), cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { area = "Admin", contextId = editor.NeedsContext ? context?.RouteId : null });
        AddError(result);
        return await Form("Create", input, null, context, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var entity = await Find(db, id, cancellationToken);
        if (entity is null) return NotFound();
        await ReadAudit([entity], id.ToString(CultureInfo.InvariantCulture), cancellationToken);
        var input = editor.Input(db, entity);
        input.RowVersion = Convert.ToBase64String(AdminEntity<TEntity, TInput>.Version(db, entity));
        return await Form("Edit", input, id, await editor.RecordContextAsync(db, entity, cancellationToken), cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromRoute] int id, TInput input, CancellationToken cancellationToken)
    {
        RejectOwnershipFields();
        var entity = await Find(db, id, cancellationToken);
        if (entity is null) return AdminConflicts.Missing(this);
        var version = AdminAudit.Version(input.RowVersion);
        if (version is null) ModelState.AddModelError(nameof(input.RowVersion), "Reload the current record before saving.");
        var context = await editor.RecordContextAsync(db, entity, cancellationToken);
        if (ModelState.IsValid)
        {
            var result = await writes.RunAsync(async database =>
            {
                var current = await Find(database, id, cancellationToken);
                if (current is null || !version!.SequenceEqual(AdminEntity<TEntity, TInput>.Version(database, current))) return WriteResult.Stale;
                var before = AdminAudit.Values(database, current);
                var validation = await editor.ApplyAsync(database, input, current,
                    await editor.RecordContextAsync(database, current, cancellationToken), false, cancellationToken);
                if (!validation.Succeeded) return validation;
                database.Entry(current).Property("RowVersion").OriginalValue = version;
                await database.SaveChangesAsync(cancellationToken);
                await AdminAudit.AddAsync(database, User, "Edit", editor.Name, id.ToString(CultureInfo.InvariantCulture), editor.PatientId(current),
                    before, AdminAudit.Values(database, current), cancellationToken: cancellationToken);
                return WriteResult.Success;
            }, cancellationToken);
            if (result.Succeeded) return RedirectToAction(nameof(Index), new { area = "Admin", contextId = editor.NeedsContext ? context?.RouteId : null });
            AddError(result);
        }
        await ReadAudit([entity], "Edit", cancellationToken);
        return await Form("Edit", input, id, context, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var entity = await Find(db, id, cancellationToken);
        if (entity is null) return NotFound();
        await ReadAudit([entity], "Delete", cancellationToken);
        return View(Shared + "Delete.cshtml", await DetailsModel(entity, cancellationToken));
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] int id, string? rowVersion, CancellationToken cancellationToken)
    {
        int? contextId = null;
        var version = AdminAudit.Version(rowVersion);
        var result = version is null ? WriteResult.Stale : await writes.RunAsync(async database =>
        {
            var entity = await Find(database, id, cancellationToken);
            if (entity is null || !version.SequenceEqual(AdminEntity<TEntity, TInput>.Version(database, entity))) return WriteResult.Stale;
            var dependents = await editor.DependentsAsync(database, entity, cancellationToken);
            if (dependents.Any(pair => pair.Value > 0)) return WriteResult.Invalid("Deletion is blocked: " + string.Join(", ", dependents.Select(pair => pair.Key + ": " + pair.Value)));
            var before = AdminAudit.Values(database, entity); var patient = editor.PatientId(entity);
            if (editor.NeedsContext) contextId = (await editor.RecordContextAsync(database, entity, cancellationToken))?.RouteId;
            database.Entry(entity).Property("RowVersion").OriginalValue = version;
            database.Set<TEntity>().Remove(entity); await database.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(database, User, "Delete", editor.Name, id.ToString(CultureInfo.InvariantCulture), patient, before: before, cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { area = "Admin", contextId });
        if (result.Conflict && !await editor.Query(db).AnyAsync(e => EF.Property<int>(e, "Id") == id, cancellationToken))
            return AdminConflicts.Missing(this);
        TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Delete), new { area = "Admin", id });
    }

    protected Task<TEntity?> Find(ApplicationDbContext database, int id, CancellationToken token) =>
        editor.Query(database).SingleOrDefaultAsync(e => EF.Property<int>(e, "Id") == id, token);
    private async Task ReadAudit(IEnumerable<TEntity> entities, string key, CancellationToken token)
    {
        if (editor.Clinical) await AdminAudit.ViewAsync(db, User, editor.Name,
            entities.Select(editor.PatientId).Where(id => id.HasValue).Select(id => id!.Value), key, token);
    }
    private async Task<AdminDetails> DetailsModel(TEntity entity, CancellationToken token) => new(editor.Title,
        AdminEntity<TEntity, TInput>.Id(db, entity), editor.Fields(db, entity), Convert.ToBase64String(AdminEntity<TEntity, TInput>.Version(db, entity)),
        await editor.RecordContextAsync(db, entity, token), await editor.DependentsAsync(db, entity, token));
    protected async Task<IActionResult> Form(string action, TInput input, int? id, OwnershipContext? context, CancellationToken token)
    {
        if (editor.Clinical && context is not null)
            await AdminAudit.ViewAsync(db, User, editor.Name, [context.Patient.Id], action, token);
        await editor.ConfigureFormAsync(db, ViewData, input, context, token);
        return View(Shared + action + ".cshtml", new AdminForm(editor.Title, input, "/Areas/Admin/Views/" + editor.Name + "/_Form.cshtml", id, context));
    }
    private void AddError(WriteResult result)
    {
        if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
        ModelState.AddModelError("", result.Error!);
    }
    private void RejectOwnershipFields()
    {
        if (!editor.Clinical || !Request.HasFormContentType) return;
        var forbidden = new[] { "PatientRecordId", "PatientUserId", "PortalUserId", "LabResultId", "PrescriptionId", "ContextId" };
        if (Request.Form.Keys.Any(key => forbidden.Contains(key.Split('.')[^1], StringComparer.OrdinalIgnoreCase)))
            ModelState.AddModelError("", "Patient and parent ownership cannot be changed by the form.");
    }
    private async Task<IActionResult> ContextSelection(string? search, int page, string action, CancellationToken token)
    {
        search = search?.Trim();
        if (search?.Length > 450) { ModelState.AddModelError(nameof(search), "Search must be at most 450 characters."); search = null; }
        var choices = await editor.SelectContextAsync(db, search, page, token);
        await AdminAudit.ViewAsync(db, User, editor.Name, choices.Rows.Where(r => r.PatientId.HasValue).Select(r => r.PatientId!.Value), action + " context", token);
        ViewData["ContextAction"] = action;
        return View(Shared + "SelectContext.cshtml", choices);
    }
}
