namespace ServiceBusExplorer.Desktop.Models;

/// <summary>
/// The environment a connection points at. Same values as the WinForms app's tag.
/// </summary>
public enum ConnectionEnvironment
{
    Untagged,
    Dev,
    Test,
    Staging,
    Prod
}

public enum EntityKind
{
    Queue,
    Topic,
    Subscription
}

/// <summary>
/// Where messages are peeked from. Scheduled messages live in the active queue, so that
/// view filters the active messages by state.
/// </summary>
public enum MessageSource
{
    Active,
    DeadLetter,
    Scheduled
}

/// <summary>
/// Counts from the runtime properties. Null where the entity kind doesn't report it
/// (topics have no active or dead-letter counts; subscriptions have no size).
/// </summary>
public sealed record EntityCounts(
    long? Active,
    long? DeadLetter,
    long? Scheduled,
    long? TransferDeadLetter,
    long? SizeInBytes);

public sealed record EntityInfo(
    EntityKind Kind,
    string Name,
    string? TopicName,
    string Status,
    bool RequiresSession,
    EntityCounts Counts,
    IReadOnlyList<EntityInfo> Subscriptions)
{
    /// <summary>The path shown to users: "queue", "topic" or "topic/subscription".</summary>
    public string Path => Kind == EntityKind.Subscription ? $"{TopicName}/{Name}" : Name;
}

public sealed record MessageSnapshot(
    long SequenceNumber,
    string? MessageId,
    string? Subject,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    DateTimeOffset EnqueuedTime,
    DateTimeOffset ScheduledEnqueueTime,
    int DeliveryCount,
    TimeSpan TimeToLive,
    DateTimeOffset ExpiresAt,
    string State,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    string? DeadLetterSource,
    IReadOnlyDictionary<string, object> ApplicationProperties,
    byte[] Body);
