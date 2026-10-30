using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusExplorer.Desktop.Models;
using ServiceBusExplorer.Desktop.Services;

namespace ServiceBusExplorer.Desktop.ViewModels;

/// <summary>
/// The selected entity: header counts plus the Messages / Dead-letter / Scheduled views.
/// Read-only: every load is a peek.
/// </summary>
public partial class EntityViewModel : ObservableObject
{
    internal const int MaxPeekCount = 1000;

    readonly INamespaceClient client;
    CancellationTokenSource? loadCancellation;

    public EntityViewModel(EntityInfo entity, INamespaceClient client)
    {
        this.client = client;
        Entity = entity;
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindLabel), nameof(Breadcrumb), nameof(ActiveText), nameof(DeadLetterText),
        nameof(ScheduledText), nameof(TransferDeadLetterText), nameof(SizeText), nameof(HasMessages),
        nameof(BrowseBlockedReason), nameof(CanBrowse), nameof(IsActiveStatus))]
    public partial EntityInfo Entity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrowseBlockedReason), nameof(CanBrowse), nameof(IsActiveSource),
        nameof(IsDeadLetterSource), nameof(IsScheduledSource), nameof(TimeColumnHeader))]
    public partial MessageSource Source { get; set; } = MessageSource.Active;

    [ObservableProperty]
    public partial MessageViewModel? SelectedMessage { get; set; }

    [ObservableProperty]
    public partial int PeekCount { get; set; } = 100;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string KindLabel => Entity.Kind.ToString();

    public string Breadcrumb => Entity.Kind switch
    {
        EntityKind.Queue => "Queues",
        EntityKind.Topic => "Topics",
        _ => $"Topics / {Entity.TopicName} / Subscriptions"
    };

    public bool IsActiveStatus => Entity.Status is "Active" or "Unknown";

    public bool HasMessages => Entity.Kind != EntityKind.Topic;

    public string ActiveText => Format(Entity.Counts.Active);

    public string DeadLetterText => Format(Entity.Counts.DeadLetter);

    public string ScheduledText => Format(Entity.Counts.Scheduled);

    public string TransferDeadLetterText => Format(Entity.Counts.TransferDeadLetter);

    public string SizeText => Entity.Counts.SizeInBytes is { } size ? MessageViewModel.FormatSize(size) : "—";

    public bool IsActiveSource => Source == MessageSource.Active;

    public bool IsDeadLetterSource => Source == MessageSource.DeadLetter;

    public bool IsScheduledSource => Source == MessageSource.Scheduled;

    public string TimeColumnHeader => Source == MessageSource.Scheduled ? "Scheduled for" : "Enqueued";

    public string? BrowseBlockedReason => Entity switch
    {
        { Kind: EntityKind.Topic } => "Topics hold no messages. Pick one of its subscriptions.",
        { RequiresSession: true } when Source != MessageSource.DeadLetter =>
            "This entity uses sessions, and browsing sessions isn't in this version yet. Its dead-letter queue works.",
        _ => null
    };

    public bool CanBrowse => BrowseBlockedReason is null;

    /// <summary>Replaces the entity's counts after a refresh, keeping the loaded messages.</summary>
    public void UpdateEntity(EntityInfo entity) => Entity = entity;

    public void CancelLoad() => loadCancellation?.Cancel();

    partial void OnSourceChanged(MessageSource value) => _ = LoadAsync();

    partial void OnPeekCountChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, MaxPeekCount);
        if (clamped != value)
        {
            PeekCount = clamped;
        }
    }

    [RelayCommand]
    void ShowSource(MessageSource source) => Source = source;

    [RelayCommand]
    Task Peek() => LoadAsync();

    /// <summary>Peeks the current source. A newer load cancels an older one.</summary>
    public async Task LoadAsync()
    {
        loadCancellation?.Cancel();
        var cancellation = loadCancellation = new CancellationTokenSource();

        Messages.Clear();
        SelectedMessage = null;
        Error = null;

        if (BrowseBlockedReason is { } reason)
        {
            Notice = reason;
            IsLoading = false;
            return;
        }

        Notice = null;
        IsLoading = true;
        try
        {
            var messages = await client.PeekAsync(Entity, Source, PeekCount, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            foreach (var message in messages)
            {
                Messages.Add(new MessageViewModel(message));
            }

            SelectedMessage = Messages.FirstOrDefault();
            Notice = DescribeResult(Messages.Count);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Superseded by a newer load.
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested)
            {
                Error = ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(loadCancellation, cancellation))
            {
                IsLoading = false;
            }
        }
    }

    string DescribeResult(int count) => Source switch
    {
        MessageSource.Scheduled => $"{count} scheduled among the first {PeekCount} messages peeked",
        MessageSource.DeadLetter => count == 0 ? "No dead-lettered messages" : $"{count} dead-lettered messages peeked",
        _ => count == 0 ? "No messages" : $"{count} messages peeked"
    } + " · nothing was removed";

    static string Format(long? value) => value is { } number ? number.ToString("N0") : "—";
}
