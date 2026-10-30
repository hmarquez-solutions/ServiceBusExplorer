using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceBusExplorer.Desktop.Models;

namespace ServiceBusExplorer.Desktop.Services;

public sealed class ServiceBusNamespaceClient : INamespaceClient
{
    readonly ServiceBusClient client;
    readonly ServiceBusAdministrationClient administrationClient;

    public ServiceBusNamespaceClient(ConnectionTarget target)
    {
        FullyQualifiedNamespace = target.FullyQualifiedNamespace;

        if (target.UsesEntra)
        {
            // One credential for both clients, so the user signs in once.
            TokenCredential credential = new InteractiveBrowserCredential(
                new InteractiveBrowserCredentialOptions { TenantId = target.TenantId });
            client = new ServiceBusClient(target.FullyQualifiedNamespace, credential);
            administrationClient = new ServiceBusAdministrationClient(target.FullyQualifiedNamespace, credential);
        }
        else
        {
            client = new ServiceBusClient(target.ConnectionString);
            administrationClient = new ServiceBusAdministrationClient(target.ConnectionString);
        }
    }

    public string FullyQualifiedNamespace { get; }

    public async Task<IReadOnlyList<EntityInfo>> GetEntitiesAsync(CancellationToken cancellationToken)
    {
        var entities = new List<EntityInfo>();

        var queueProperties = new Dictionary<string, QueueProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var queue in administrationClient.GetQueuesAsync(cancellationToken))
        {
            queueProperties[queue.Name] = queue;
        }

        await foreach (var runtime in administrationClient.GetQueuesRuntimePropertiesAsync(cancellationToken))
        {
            queueProperties.TryGetValue(runtime.Name, out var queue);
            entities.Add(new EntityInfo(
                EntityKind.Queue,
                runtime.Name,
                TopicName: null,
                queue?.Status.ToString() ?? "Unknown",
                queue?.RequiresSession ?? false,
                new EntityCounts(runtime.ActiveMessageCount, runtime.DeadLetterMessageCount, runtime.ScheduledMessageCount,
                    runtime.TransferDeadLetterMessageCount, runtime.SizeInBytes),
                Array.Empty<EntityInfo>()));
        }

        var topicRuntime = new Dictionary<string, TopicRuntimeProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var runtime in administrationClient.GetTopicsRuntimePropertiesAsync(cancellationToken))
        {
            topicRuntime[runtime.Name] = runtime;
        }

        await foreach (var topic in administrationClient.GetTopicsAsync(cancellationToken))
        {
            var subscriptions = await GetSubscriptionsAsync(topic.Name, cancellationToken);
            topicRuntime.TryGetValue(topic.Name, out var runtime);
            entities.Add(new EntityInfo(
                EntityKind.Topic,
                topic.Name,
                TopicName: null,
                topic.Status.ToString(),
                RequiresSession: false,
                new EntityCounts(Active: null, DeadLetter: null, runtime?.ScheduledMessageCount, TransferDeadLetter: null,
                    runtime?.SizeInBytes),
                subscriptions));
        }

        return entities;
    }

    async Task<IReadOnlyList<EntityInfo>> GetSubscriptionsAsync(string topicName, CancellationToken cancellationToken)
    {
        var subscriptionProperties = new Dictionary<string, SubscriptionProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var subscription in administrationClient.GetSubscriptionsAsync(topicName, cancellationToken))
        {
            subscriptionProperties[subscription.SubscriptionName] = subscription;
        }

        var subscriptions = new List<EntityInfo>();
        await foreach (var runtime in administrationClient.GetSubscriptionsRuntimePropertiesAsync(topicName, cancellationToken))
        {
            subscriptionProperties.TryGetValue(runtime.SubscriptionName, out var subscription);
            subscriptions.Add(new EntityInfo(
                EntityKind.Subscription,
                runtime.SubscriptionName,
                topicName,
                subscription?.Status.ToString() ?? "Unknown",
                subscription?.RequiresSession ?? false,
                new EntityCounts(runtime.ActiveMessageCount, runtime.DeadLetterMessageCount, Scheduled: null,
                    runtime.TransferDeadLetterMessageCount, SizeInBytes: null),
                Array.Empty<EntityInfo>()));
        }

        return subscriptions;
    }

    public async Task<IReadOnlyList<MessageSnapshot>> PeekAsync(EntityInfo entity, MessageSource source, int maxCount,
        CancellationToken cancellationToken)
    {
        if (entity.Kind == EntityKind.Topic)
        {
            throw new InvalidOperationException("Topics hold no messages; peek one of their subscriptions.");
        }

        var options = new ServiceBusReceiverOptions
        {
            SubQueue = source == MessageSource.DeadLetter ? SubQueue.DeadLetter : SubQueue.None
        };

        await using var receiver = entity.Kind == EntityKind.Queue
            ? client.CreateReceiver(entity.Name, options)
            : client.CreateReceiver(entity.TopicName, entity.Name, options);

        // A single peek can return fewer messages than asked for; keep going until we have
        // maxCount or the entity runs out. The receiver continues from the last sequence number.
        var peeked = new List<ServiceBusReceivedMessage>();
        while (peeked.Count < maxCount)
        {
            var batch = await receiver.PeekMessagesAsync(maxCount - peeked.Count, cancellationToken: cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            peeked.AddRange(batch);
        }

        return peeked
            .Where(message => source switch
            {
                MessageSource.Scheduled => message.State == ServiceBusMessageState.Scheduled,
                MessageSource.Active => message.State != ServiceBusMessageState.Scheduled,
                _ => true
            })
            .Select(ToSnapshot)
            .ToList();
    }

    internal static MessageSnapshot ToSnapshot(ServiceBusReceivedMessage message)
    {
        return new MessageSnapshot(
            message.SequenceNumber,
            message.MessageId,
            message.Subject,
            message.ContentType,
            message.CorrelationId,
            message.SessionId,
            message.EnqueuedTime,
            message.ScheduledEnqueueTime,
            message.DeliveryCount,
            message.TimeToLive,
            message.ExpiresAt,
            message.State.ToString(),
            message.DeadLetterReason,
            message.DeadLetterErrorDescription,
            message.DeadLetterSource,
            message.ApplicationProperties.ToDictionary(pair => pair.Key, pair => pair.Value),
            message.Body.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
