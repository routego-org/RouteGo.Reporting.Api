namespace RouteGo.Reporting.Api.Common.Messaging;

/// <summary>
/// One slice's worker for the event types it applies. Several slices read the one reporting queue, so
/// the queue service dispatches each event to the worker that names its type, and the slices never
/// call each other.
/// </summary>
public interface IEventWorker
{
    /// <summary>The event types this worker applies.</summary>
    IReadOnlyList<string> EventTypes { get; }

    /// <summary>Applies one event and says what should happen to its queue message.</summary>
    /// <param name="receivedEvent">The event, read from the RouteGo envelope.</param>
    /// <param name="cancellationToken">Cancelled when the host is stopping.</param>
    Task<EventOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken);
}
