using ServiceBusExplorer.Desktop.Models;

namespace ServiceBusExplorer.Desktop.Services;

/// <summary>
/// Read-only access to one namespace. Nothing here can change or remove a message or an entity.
/// </summary>
public interface INamespaceClient : IAsyncDisposable
{
    string FullyQualifiedNamespace { get; }

    /// <summary>Queues, then topics with their subscriptions, each with runtime counts.</summary>
    Task<IReadOnlyList<EntityInfo>> GetEntitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Peeks up to <paramref name="maxCount"/> messages without locking or removing them.
    /// For <see cref="MessageSource.Scheduled"/> and <see cref="MessageSource.Active"/>, the same
    /// peeked messages are split by state, so each view shows what's among the first maxCount.
    /// </summary>
    Task<IReadOnlyList<MessageSnapshot>> PeekAsync(EntityInfo entity, MessageSource source, int maxCount,
        CancellationToken cancellationToken);
}
