using ServiceBusExplorer.Desktop.Models;

namespace ServiceBusExplorer.Desktop.ViewModels;

/// <summary>
/// A row in the navigator: either a group header ("Queues", "Topics") or an entity.
/// </summary>
public sealed class EntityNodeViewModel
{
    EntityNodeViewModel(string title, EntityInfo? entity, IReadOnlyList<EntityNodeViewModel> children)
    {
        Title = title;
        Entity = entity;
        Children = children;
    }

    public static EntityNodeViewModel Group(string title, IReadOnlyList<EntityNodeViewModel> children) =>
        new(title, entity: null, children);

    public static EntityNodeViewModel ForEntity(EntityInfo entity) =>
        new(entity.Name, entity, entity.Subscriptions.Select(ForEntity).ToList());

    public string Title { get; }

    public EntityInfo? Entity { get; }

    public IReadOnlyList<EntityNodeViewModel> Children { get; }

    public bool IsGroup => Entity is null;

    public string ActiveText => Entity?.Counts.Active is { } active ? active.ToString("N0") : string.Empty;

    public long DeadLetterCount => Entity?.Counts.DeadLetter ?? 0;

    public bool HasDeadLetters => DeadLetterCount > 0;

    public string DeadLetterText => HasDeadLetters ? DeadLetterCount.ToString("N0") : string.Empty;

    public string GroupCountText => IsGroup ? Children.Count.ToString() : string.Empty;

    public bool IsDisabled => Entity is { Status: not "Active" and not "Unknown" };

    public bool RequiresSession => Entity?.RequiresSession ?? false;
}
