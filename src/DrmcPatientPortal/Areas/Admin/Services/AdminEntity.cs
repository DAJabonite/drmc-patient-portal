using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public abstract class AdminEntity<TEntity, TInput> where TEntity : class, new() where TInput : class, IAdminInput, new()
{
    public abstract string Name { get; }
    public abstract string Title { get; }
    public abstract string SearchColumn { get; }
    public virtual bool Clinical => false;
    public virtual bool NeedsContext => false;
    public virtual bool RequiresListContext => false;
    public virtual Task<AdminList> SelectContextAsync(ApplicationDbContext db, string? search, int page, CancellationToken token) =>
        throw new InvalidOperationException("This editor does not select an ownership context.");
    public virtual IQueryable<TEntity> Query(ApplicationDbContext db, int? contextId = null) => db.Set<TEntity>();
    public virtual IQueryable<TEntity> Search(IQueryable<TEntity> query, string search) => query.Where(e =>
        EF.Functions.Collate(EF.Property<string>(e, SearchColumn), "Latin1_General_100_CI_AS").Contains(search));
    public virtual string Label(ApplicationDbContext db, TEntity entity) => db.Entry(entity).Property(SearchColumn).CurrentValue?.ToString() ?? "";
    public virtual string Summary(TEntity entity) => "";
    public virtual int? PatientId(TEntity entity) => null;
    public virtual Task<OwnershipContext?> CreateContextAsync(ApplicationDbContext db, int routeId, CancellationToken token) => Task.FromResult<OwnershipContext?>(null);
    public virtual Task<OwnershipContext?> RecordContextAsync(ApplicationDbContext db, TEntity entity, CancellationToken token) => Task.FromResult<OwnershipContext?>(null);
    public virtual Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, TEntity entity, CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
    public virtual Task ConfigureFormAsync(ApplicationDbContext db, Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary viewData,
        TInput input, OwnershipContext? context, CancellationToken token) => Task.CompletedTask;
    public abstract TInput Input(ApplicationDbContext db, TEntity entity);
    public abstract Task<WriteResult> ApplyAsync(ApplicationDbContext db, TInput input, TEntity entity,
        OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null);

    public virtual IReadOnlyList<RecordField> Fields(ApplicationDbContext db, TEntity entity) => db.Entry(entity).Properties
        .Where(p => p.Metadata.Name is not "RowVersion" and not "Id")
        .Select(p => new RecordField(p.Metadata.Name, p.CurrentValue switch
        {
            DateTime time => time.ToString("yyyy-MM-dd HH:mm:ss"), null => "unassigned", _ => p.CurrentValue.ToString() ?? ""
        })).ToArray();

    public static int Id(ApplicationDbContext db, TEntity entity) => (int)db.Entry(entity).Property("Id").CurrentValue!;
    public static byte[] Version(ApplicationDbContext db, TEntity entity) => (byte[])db.Entry(entity).Property("RowVersion").CurrentValue!;
    public static DateTime WallTime(DateTime value, DateTime? current = null)
    {
        // Native date/time inputs retain milliseconds, not SQL Server's seven-digit precision.
        if (current.HasValue && value.Ticks % TimeSpan.TicksPerMillisecond == 0 &&
            value.Ticks / TimeSpan.TicksPerMillisecond == current.Value.Ticks / TimeSpan.TicksPerMillisecond)
            value = current.Value;
        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }
}
