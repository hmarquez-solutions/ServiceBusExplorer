using System.Collections.ObjectModel;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceBusExplorer.Desktop.Models;
using ServiceBusExplorer.Desktop.Services;

namespace ServiceBusExplorer.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    readonly Func<ConnectionTarget, INamespaceClient> clientFactory;
    INamespaceClient? client;
    IReadOnlyList<EntityInfo> allEntities = Array.Empty<EntityInfo>();

    public MainWindowViewModel(Func<ConnectionTarget, INamespaceClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public ConnectViewModel Connect { get; } = new();

    public ObservableCollection<EntityNodeViewModel> Nodes { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(IsDisconnected))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not connected";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial string NamespaceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FullyQualifiedNamespace { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(EnvironmentLabel), nameof(EnvironmentColor))]
    public partial ConnectionEnvironment Environment { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OnlyWithDeadLetters { get; set; }

    [ObservableProperty]
    public partial EntityNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial EntityViewModel? CurrentEntity { get; set; }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; } = true;

    public bool IsDisconnected => !IsConnected;

    public string WindowTitle => IsConnected
        ? $"[{EnvironmentLabel}] {NamespaceName} - Service Bus Explorer"
        : "Service Bus Explorer";

    public string EnvironmentLabel => Environment.ToString().ToUpperInvariant();

    /// <summary>Same colors as the WinForms band.</summary>
    public string EnvironmentColor => Environment switch
    {
        ConnectionEnvironment.Dev => "#0F7B4F",
        ConnectionEnvironment.Test => "#005FB8",
        ConnectionEnvironment.Staging => "#CA8A04",
        ConnectionEnvironment.Prod => "#C42B1C",
        _ => "#6E6E6E"
    };

    [RelayCommand]
    async Task ConnectAsync()
    {
        if (IsBusy || !Connect.TryCreateTarget(out var target))
        {
            return;
        }

        // Read the tag now: the picker stays editable while signing in.
        var environment = Connect.Environment;
        IsBusy = true;
        StatusText = target!.UsesEntra ? "Signing in and listing entities..." : "Listing entities...";
        INamespaceClient? newClient = null;
        try
        {
            newClient = clientFactory(target);
            var entities = await newClient.GetEntitiesAsync(CancellationToken.None);

            await DisposeClientAsync();
            client = newClient;
            newClient = null;

            allEntities = entities;
            NamespaceName = target.NamespaceName;
            FullyQualifiedNamespace = target.FullyQualifiedNamespace;
            Environment = environment;
            SelectedNode = null;
            CurrentEntity = null;
            FilterText = string.Empty;
            OnlyWithDeadLetters = false;
            RebuildTree();
            IsConnected = true;
            StatusText = Summarize(entities);
        }
        catch (Exception ex)
        {
            Connect.Error = Describe(ex);
            StatusText = IsConnected ? StatusText : "Not connected";
        }
        finally
        {
            if (newClient is not null)
            {
                await newClient.DisposeAsync();
            }

            IsBusy = false;
        }
    }

    [RelayCommand]
    async Task DisconnectAsync()
    {
        CurrentEntity?.CancelLoad();
        await DisposeClientAsync();
        allEntities = Array.Empty<EntityInfo>();
        Nodes.Clear();
        SelectedNode = null;
        CurrentEntity = null;
        IsConnected = false;
        StatusText = "Not connected";
    }

    [RelayCommand]
    async Task RefreshAsync()
    {
        if (client is null || IsBusy)
        {
            return;
        }

        var refreshedClient = client;
        IsBusy = true;
        StatusText = "Refreshing...";
        try
        {
            var entities = await refreshedClient.GetEntitiesAsync(CancellationToken.None);

            // Disconnected while waiting: drop the results.
            if (!ReferenceEquals(client, refreshedClient))
            {
                return;
            }

            allEntities = entities;
            RebuildTree();

            if (CurrentEntity is not null && Find(allEntities, CurrentEntity.Entity) is { } updated)
            {
                CurrentEntity.UpdateEntity(updated);
            }

            StatusText = Summarize(allEntities);
        }
        catch (Exception ex) when (ReferenceEquals(client, refreshedClient))
        {
            StatusText = $"Refresh failed: {Describe(ex)}";
        }
        catch (Exception)
        {
            // Disconnected while waiting; the client was disposed under the refresh.
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    partial void OnFilterTextChanged(string value) => RebuildTree();

    partial void OnOnlyWithDeadLettersChanged(bool value) => RebuildTree();

    partial void OnSelectedNodeChanged(EntityNodeViewModel? value)
    {
        // Group headers, and the deselection that happens when the tree is rebuilt, keep the current view.
        if (value?.Entity is not { } entity || client is null)
        {
            return;
        }

        if (CurrentEntity is not null && SameEntity(CurrentEntity.Entity, entity))
        {
            CurrentEntity.UpdateEntity(entity);
            return;
        }

        CurrentEntity?.CancelLoad();
        CurrentEntity = new EntityViewModel(entity, client);
        _ = CurrentEntity.LoadAsync();
    }

    void RebuildTree()
    {
        var visible = EntityTreeFilter.Apply(allEntities, FilterText, OnlyWithDeadLetters);

        Nodes.Clear();
        Nodes.Add(EntityNodeViewModel.Group("Queues",
            visible.Where(entity => entity.Kind == EntityKind.Queue).Select(EntityNodeViewModel.ForEntity).ToList()));
        Nodes.Add(EntityNodeViewModel.Group("Topics",
            visible.Where(entity => entity.Kind == EntityKind.Topic).Select(EntityNodeViewModel.ForEntity).ToList()));
    }

    async Task DisposeClientAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
            client = null;
        }
    }

    static EntityInfo? Find(IEnumerable<EntityInfo> entities, EntityInfo target)
    {
        foreach (var entity in entities)
        {
            if (SameEntity(entity, target))
            {
                return entity;
            }

            if (Find(entity.Subscriptions, target) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    static bool SameEntity(EntityInfo left, EntityInfo right) =>
        left.Kind == right.Kind && string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);

    static string Summarize(IReadOnlyList<EntityInfo> entities)
    {
        var queues = entities.Count(entity => entity.Kind == EntityKind.Queue);
        var topics = entities.Count(entity => entity.Kind == EntityKind.Topic);
        var subscriptions = entities.Sum(entity => entity.Subscriptions.Count);
        return $"{queues} queues · {topics} topics · {subscriptions} subscriptions · refreshed {DateTime.Now:HH:mm:ss}";
    }

    internal static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            "Access denied. Listing entities needs Manage rights (SAS) or the Azure Service Bus Data Owner role (Entra ID).",
        AuthenticationFailedException => $"Sign-in failed: {ex.Message}",
        ServiceBusException { Reason: ServiceBusFailureReason.ServiceCommunicationProblem } =>
            "Couldn't reach the namespace. Check the name and your network.",
        _ => ex.Message
    };
}
