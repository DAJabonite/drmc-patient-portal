namespace DrmcPatientPortal.Services;

public sealed class AuditLogPersistenceException(string message, Exception innerException) : Exception(message, innerException);
