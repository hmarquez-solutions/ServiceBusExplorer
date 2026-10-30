using CommunityToolkit.Mvvm.ComponentModel;
using ServiceBusExplorer.Desktop.Models;
using ServiceBusExplorer.Desktop.Services;

namespace ServiceBusExplorer.Desktop.ViewModels;

public partial class ConnectViewModel : ObservableObject
{
    public IReadOnlyList<ConnectionEnvironment> Environments { get; } = Enum.GetValues<ConnectionEnvironment>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseConnectionString))]
    public partial bool UseEntra { get; set; }

    [ObservableProperty]
    public partial string ConnectionString { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NamespaceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TenantId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ConnectionEnvironment Environment { get; set; } = ConnectionEnvironment.Untagged;

    [ObservableProperty]
    public partial string? Error { get; set; }

    // The two radio buttons bind to opposite sides of the same choice.
    public bool UseConnectionString
    {
        get => !UseEntra;
        set => UseEntra = !value;
    }

    public bool TryCreateTarget(out ConnectionTarget? target)
    {
        var created = UseEntra
            ? ConnectionTarget.TryFromNamespace(NamespaceName, TenantId, out target, out var error)
            : ConnectionTarget.TryFromConnectionString(ConnectionString, out target, out error);

        Error = created ? null : error;
        return created;
    }
}
