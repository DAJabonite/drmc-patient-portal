using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

// SECURITY REVIEW TODO (Item #1): Asynchronous Care Communication & Message Storage
// BOUNDARY NOTE: Asynchronous communication threads between patient and clinical staff are fully operational.
// Production hardening requirements:
// 1. Column-level encryption-at-rest (AES-256) for Message.Body and Subject lines.
// 2. Verified role-based clinical staff assignment policy.
// 3. Automated rate limiting to prevent staff inbox flood.
// Reference: docs/SECURITY_REVIEW_TODO.md
[Authorize]
[Route("Patient/Messages")]
public class MessagesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public MessagesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    // GET /Patient/Messages
    [HttpGet("")]
    public async Task<IActionResult> Index(MessageCategory? category)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var query = _db.MessageThreads
            .Include(t => t.Messages)
            .Where(t => t.PatientUserId == user.Id)
            .AsQueryable();

        if (category.HasValue)
        {
            query = query.Where(t => t.Category == category.Value);
        }

        var threads = await query
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync();

        var unreadCount = threads.SelectMany(t => t.Messages)
            .Count(m => !m.IsRead && m.SenderRole != MessageSenderRole.Patient);

        var model = new MessagesIndexViewModel
        {
            SelectedCategory = category,
            Threads = threads,
            UnreadCount = unreadCount
        };

        return View(model);
    }

    // GET /Patient/Messages/Thread/{id}
    [HttpGet("Thread/{id:int}")]
    public async Task<IActionResult> Thread(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var thread = await _db.MessageThreads
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id && t.PatientUserId == user.Id);

        if (thread is null)
        {
            return NotFound();
        }

        // Mark unread staff messages in this thread as read
        var unreadStaffMessages = thread.Messages
            .Where(m => !m.IsRead && m.SenderRole != MessageSenderRole.Patient)
            .ToList();

        if (unreadStaffMessages.Count > 0)
        {
            foreach (var m in unreadStaffMessages)
            {
                m.IsRead = true;
                m.ReadAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_MESSAGE_THREAD", $"MessageThread/{id}", $"Subject: {thread.Subject} ({thread.Department})", ip);

        return View(thread);
    }

    // GET /Patient/Messages/New
    [HttpGet("New")]
    public IActionResult New(string? department, string? subject)
    {
        var model = new NewMessageViewModel
        {
            Department = department ?? "Internal Medicine",
            Subject = subject ?? string.Empty,
            Departments = ClinicalDepartments.All
        };

        return View(model);
    }

    // POST /Patient/Messages/Create
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NewMessageViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Body))
        {
            ModelState.AddModelError(string.Empty, "Please enter both a subject and message body.");
            model.Departments = ClinicalDepartments.All;
            return View("New", model);
        }

        var thread = new MessageThread
        {
            PatientUserId = user.Id,
            Department = model.Department,
            Subject = model.Subject.Trim(),
            Category = model.Category,
            Status = ThreadStatus.Open,
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow
        };

        thread.Messages.Add(new Message
        {
            SenderUserId = user.Id,
            SenderName = user.FullName,
            SenderRole = MessageSenderRole.Patient,
            Body = model.Body.Trim(),
            SentAt = DateTime.UtcNow,
            IsRead = true
        });

        _db.MessageThreads.Add(thread);
        await _db.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "CREATE_MESSAGE_THREAD", $"MessageThread/{thread.Id}", $"Thread created for {thread.Department}: {thread.Subject}", ip);

        TempData["SuccessMessage"] = "Your message has been sent to the clinic coordinator.";
        return RedirectToAction(nameof(Thread), new { id = thread.Id });
    }

    // POST /Patient/Messages/Reply
    [HttpPost("Reply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int threadId, string replyBody)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var thread = await _db.MessageThreads
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == threadId && t.PatientUserId == user.Id);

        if (thread is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(replyBody))
        {
            return RedirectToAction(nameof(Thread), new { id = threadId });
        }

        var msg = new Message
        {
            ThreadId = thread.Id,
            SenderUserId = user.Id,
            SenderName = user.FullName,
            SenderRole = MessageSenderRole.Patient,
            Body = replyBody.Trim(),
            SentAt = DateTime.UtcNow,
            IsRead = true
        };

        thread.LastMessageAt = DateTime.UtcNow;
        if (thread.Status == ThreadStatus.Resolved || thread.Status == ThreadStatus.Closed)
        {
            thread.Status = ThreadStatus.Open;
        }

        thread.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "REPLY_MESSAGE_THREAD", $"MessageThread/{thread.Id}", $"Reply posted to thread {thread.Id}", ip);

        return RedirectToAction(nameof(Thread), new { id = threadId });
    }
}

public class MessagesIndexViewModel
{
    public MessageCategory? SelectedCategory { get; set; }
    public IReadOnlyList<MessageThread> Threads { get; set; } = Array.Empty<MessageThread>();
    public int UnreadCount { get; set; }
}

public class NewMessageViewModel
{
    public string Department { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public MessageCategory Category { get; set; } = MessageCategory.GeneralInquiry;
    public string Body { get; set; } = string.Empty;
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
}
