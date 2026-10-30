using ServiceBusExplorer.Desktop.Models;
using ServiceBusExplorer.Desktop.Services;

namespace ServiceBusExplorer.Desktop.Tests;

sealed class FakeNamespaceClient : INamespaceClient
{
    public FakeNamespaceClient(string fullyQualifiedNamespace = "contoso.servicebus.windows.net")
    {
        FullyQualifiedNamespace = fullyQualifiedNamespace;
    }

    public string FullyQualifiedNamespace { get; }

    public IReadOnlyList<EntityInfo> Entities { get; set; } = Array.Empty<EntityInfo>();

    public Exception? GetEntitiesFailure { get; set; }

    /// <summary>When set, GetEntitiesAsync waits for it, so a test can act mid-call.</summary>
    public TaskCompletionSource? EntitiesGate { get; set; }

    public Func<EntityInfo, MessageSource, IReadOnlyList<MessageSnapshot>> Messages { get; set; } =
        (_, _) => Array.Empty<MessageSnapshot>();

    public List<(string Path, MessageSource Source, int MaxCount)> Peeks { get; } = new();

    public bool IsDisposed { get; private set; }

    public async Task<IReadOnlyList<EntityInfo>> GetEntitiesAsync(CancellationToken cancellationToken)
    {
        if (EntitiesGate is { } gate)
        {
            await gate.Task;
        }

        return GetEntitiesFailure is { } failure ? throw failure : Entities;
    }

    public Task<IReadOnlyList<MessageSnapshot>> PeekAsync(EntityInfo entity, MessageSource source, int maxCount,
        CancellationToken cancellationToken)
    {
        Peeks.Add((entity.Path, source, maxCount));
        return Task.FromResult(Messages(entity, source));
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

static class TestData
{
    public static EntityInfo Queue(string name, long active = 0, long deadLetter = 0, bool requiresSession = false) =>
        new(EntityKind.Queue, name, null, "Active", requiresSession,
            new EntityCounts(active, deadLetter, 0, 0, 1024), Array.Empty<EntityInfo>());

    public static EntityInfo Subscription(string topic, string name, long active = 0, long deadLetter = 0) =>
        new(EntityKind.Subscription, name, topic, "Active", false,
            new EntityCounts(active, deadLetter, null, 0, null), Array.Empty<EntityInfo>());

    public static EntityInfo Topic(string name, params EntityInfo[] subscriptions) =>
        new(EntityKind.Topic, name, null, "Active", false,
            new EntityCounts(null, null, 0, null, 2048), subscriptions);

    public static MessageSnapshot Message(long sequenceNumber, string? deadLetterReason = null, string body = "{\"id\":1}") =>
        new(sequenceNumber, $"msg-{sequenceNumber}", "OrderCreated", "application/json", null, null,
            new DateTimeOffset(2026, 10, 28, 9, 14, 3, TimeSpan.Zero), default, 1, TimeSpan.MaxValue, DateTimeOffset.MaxValue,
            "Active", deadLetterReason, deadLetterReason is null ? null : "Gave up", deadLetterReason is null ? null : "orders",
            new Dictionary<string, object> { ["tenant"] = "east" }, System.Text.Encoding.UTF8.GetBytes(body));
}
