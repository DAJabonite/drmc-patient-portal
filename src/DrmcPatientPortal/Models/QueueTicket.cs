namespace DrmcPatientPortal.Models;

public enum QueueTicketStatus
{
    Waiting,
    Called,
    Serving,
    Completed,
    Delayed
}

public class QueueTicket
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty; // e.g. "IM-104", "PED-018"
    public string Department { get; set; } = string.Empty;
    public string ClinicRoom { get; set; } = string.Empty;   // e.g. "Room 102 - OPD Building"
    public QueueTicketStatus Status { get; set; } = QueueTicketStatus.Waiting;
    public bool IsPriority { get; set; } // Senior, PWD, Pregnant, Pediatric
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CalledAt { get; set; }
    public DateTime? ServedAt { get; set; }
    public int EstimatedWaitMinutes { get; set; }
    
    // Optional linkage to registered patient account (if ticket was booked/claimed by a user)
    public string? PatientUserId { get; set; }
    public ApplicationUser? Patient { get; set; }
}
