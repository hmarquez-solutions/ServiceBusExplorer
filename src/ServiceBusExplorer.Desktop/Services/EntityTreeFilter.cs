using ServiceBusExplorer.Desktop.Models;

namespace ServiceBusExplorer.Desktop.Services;

/// <summary>
/// Filters the navigator the same way the WinForms tree filter does: a matching topic keeps all
/// its subscriptions; a matching subscription keeps its topic but hides the other subscriptions.
/// </summary>
public static class EntityTreeFilter
{
    public static IReadOnlyList<EntityInfo> Apply(IReadOnlyList<EntityInfo> entities, string? filter, bool onlyWithDeadLetters)
    {
        var text = filter?.Trim() ?? string.Empty;
        var result = new List<EntityInfo>();

        foreach (var entity in entities)
        {
            if (entity.Kind != EntityKind.Topic)
            {
                if (Matches(entity, text, onlyWithDeadLetters))
                {
                    result.Add(entity);
                }

                continue;
            }

            var topicMatchesText = NameMatches(entity, text);
            var subscriptions = entity.Subscriptions
                .Where(subscription => (topicMatchesText || NameMatches(subscription, text)) &&
                                       (!onlyWithDeadLetters || HasDeadLetters(subscription)))
                .ToList();

            var keepTopic = onlyWithDeadLetters ? subscriptions.Count > 0 : topicMatchesText || subscriptions.Count > 0;
            if (keepTopic)
            {
                result.Add(entity with { Subscriptions = subscriptions });
            }
        }

        return result;
    }

    static bool Matches(EntityInfo entity, string text, bool onlyWithDeadLetters) =>
        NameMatches(entity, text) && (!onlyWithDeadLetters || HasDeadLetters(entity));

    static bool NameMatches(EntityInfo entity, string text) =>
        text.Length == 0 || entity.Name.Contains(text, StringComparison.OrdinalIgnoreCase);

    static bool HasDeadLetters(EntityInfo entity) => entity.Counts.DeadLetter > 0;
}
