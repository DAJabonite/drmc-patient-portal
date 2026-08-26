using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public class QueueController : Controller
{
    private readonly ApplicationDbContext _db;

    public QueueController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /Queue
    public async Task<IActionResult> Index(string? department)
    {
        var query = _db.QueueTickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(t => t.Department == department);
        }

        var tickets = await query
            .OrderByDescending(t => t.CalledAt ?? t.IssuedAt)
            .ToListAsync();

        var departmentNames = ClinicalDepartments.All.Select(d => d.Name).ToList();

        var summaries = new List<DepartmentQueueSummary>();
        foreach (var dept in departmentNames)
        {
            var deptTickets = tickets.Where(t => t.Department == dept).ToList();
            var serving = deptTickets.FirstOrDefault(t => t.Status == QueueTicketStatus.Serving);
            var called = deptTickets.FirstOrDefault(t => t.Status == QueueTicketStatus.Called);
            var waiting = deptTickets.Where(t => t.Status == QueueTicketStatus.Waiting).OrderBy(t => t.IssuedAt).ToList();

            summaries.Add(new DepartmentQueueSummary
            {
                Department = dept,
                ClinicRoom = serving?.ClinicRoom ?? called?.ClinicRoom ?? (dept == "Internal Medicine" ? "Room 201" : "OPD Wing"),
                ServingTicket = serving,
                CalledTicket = called,
                WaitingTickets = waiting,
                WaitingCount = waiting.Count
            });
        }

        var model = new QueueBoardViewModel
        {
            SelectedDepartment = department,
            Departments = departmentNames,
            DepartmentSummaries = summaries,
            LastUpdated = DateTime.Now
        };

        return View(model);
    }

    // GET /Queue/Status?ticketNumber=IM-105
    public async Task<IActionResult> Status(string? ticketNumber)
    {
        if (string.IsNullOrWhiteSpace(ticketNumber))
        {
            return RedirectToAction(nameof(Index));
        }

        var clean = ticketNumber.Trim().ToUpperInvariant();
        var ticket = await _db.QueueTickets
            .FirstOrDefaultAsync(t => t.TicketNumber.ToUpper() == clean);

        if (ticket is null)
        {
            ViewBag.NotFoundNumber = ticketNumber;
            return View("TicketNotFound");
        }

        var peopleAhead = await _db.QueueTickets
            .CountAsync(t => t.Department == ticket.Department 
                             && t.Status == QueueTicketStatus.Waiting 
                             && t.IssuedAt < ticket.IssuedAt);

        var model = new TicketStatusViewModel
        {
            Ticket = ticket,
            PeopleAhead = peopleAhead,
            EstimatedWaitMinutes = ticket.Status == QueueTicketStatus.Waiting ? Math.Max(5, (peopleAhead + 1) * 12) : 0
        };

        return View(model);
    }

    // GET /Queue/Live (JSON endpoint for low-bandwidth polling)
    [HttpGet]
    public async Task<IActionResult> Live()
    {
        var activeTickets = await _db.QueueTickets
            .Where(t => t.Status == QueueTicketStatus.Serving || t.Status == QueueTicketStatus.Called || t.Status == QueueTicketStatus.Waiting)
            .OrderByDescending(t => t.CalledAt ?? t.IssuedAt)
            .ToListAsync();

        var summaries = ClinicalDepartments.All.Select(dept =>
        {
            var deptTickets = activeTickets.Where(t => t.Department == dept.Name).ToList();
            var serving = deptTickets.FirstOrDefault(t => t.Status == QueueTicketStatus.Serving)?.TicketNumber ?? "--";
            var called = deptTickets.FirstOrDefault(t => t.Status == QueueTicketStatus.Called)?.TicketNumber ?? "--";
            var waitingCount = deptTickets.Count(t => t.Status == QueueTicketStatus.Waiting);

            return new
            {
                department = dept.Name,
                serving,
                called,
                waitingCount
            };
        });

        return Json(new
        {
            timestamp = DateTime.Now.ToString("h:mm:ss tt"),
            departments = summaries
        });
    }
}

public class QueueBoardViewModel
{
    public string? SelectedDepartment { get; set; }
    public IReadOnlyList<string> Departments { get; set; } = Array.Empty<string>();
    public IReadOnlyList<DepartmentQueueSummary> DepartmentSummaries { get; set; } = Array.Empty<DepartmentQueueSummary>();
    public DateTime LastUpdated { get; set; }
}

public class DepartmentQueueSummary
{
    public string Department { get; set; } = string.Empty;
    public string ClinicRoom { get; set; } = string.Empty;
    public QueueTicket? ServingTicket { get; set; }
    public QueueTicket? CalledTicket { get; set; }
    public IReadOnlyList<QueueTicket> WaitingTickets { get; set; } = Array.Empty<QueueTicket>();
    public int WaitingCount { get; set; }
}

public class TicketStatusViewModel
{
    public QueueTicket Ticket { get; set; } = null!;
    public int PeopleAhead { get; set; }
    public int EstimatedWaitMinutes { get; set; }
}
