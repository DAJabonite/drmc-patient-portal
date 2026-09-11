namespace DrmcPatientPortal.Infrastructure.SourceStates;

public enum SourceDataState
{
    Loading,
    Available,
    ConfirmedEmpty,
    Stale,
    Unavailable,
    Offline,
    Error
}

public sealed record SourceDataResult<T>(
    SourceDataState State,
    T? Data,
    DateTimeOffset? LastSuccessfulUpdate,
    string? Message)
{
    public bool HasUsableData => State is SourceDataState.Available or SourceDataState.Stale;

    public static SourceDataResult<T> Available(T data, DateTimeOffset updatedAt)
        => new(SourceDataState.Available, data, updatedAt, null);

    public static SourceDataResult<T> ConfirmedEmpty(DateTimeOffset updatedAt, string? message = null)
        => new(SourceDataState.ConfirmedEmpty, default, updatedAt, message);

    public static SourceDataResult<T> Stale(T data, DateTimeOffset lastSuccessfulUpdate, string message)
        => new(SourceDataState.Stale, data, lastSuccessfulUpdate, message);

    public static SourceDataResult<T> Unavailable(string message, DateTimeOffset? lastSuccessfulUpdate = null)
        => new(SourceDataState.Unavailable, default, lastSuccessfulUpdate, message);

    public static SourceDataResult<T> Offline(string message, DateTimeOffset? lastSuccessfulUpdate = null)
        => new(SourceDataState.Offline, default, lastSuccessfulUpdate, message);

    public static SourceDataResult<T> Error(string message, DateTimeOffset? lastSuccessfulUpdate = null)
        => new(SourceDataState.Error, default, lastSuccessfulUpdate, message);
}
