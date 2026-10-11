using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed record PortalPagination(int Total, int Page, string Parameter = "page")
{
    public const int Size = 25;
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)Size));
    public int Offset => (Page - 1) * Size;
    public static async Task<PortalPagination> CreateAsync<T>(IQueryable<T> query, ApplicationDbContext db, int page, string parameter = "page")
    {
        var total = await SqlReadRetry.RunAsync(db, () => query.CountAsync());
        return new(total, Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)Size))), parameter);
    }
}
