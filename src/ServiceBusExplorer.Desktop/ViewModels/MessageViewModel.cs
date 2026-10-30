using System.Globalization;
using ServiceBusExplorer.Desktop.Models;
using ServiceBusExplorer.Desktop.Services;

namespace ServiceBusExplorer.Desktop.ViewModels;

public sealed record PropertyRow(string Name, string Value);

public sealed class MessageViewModel
{
    readonly Lazy<FormattedBody> body;

    public MessageViewModel(MessageSnapshot message)
    {
        Message = message;
        body = new Lazy<FormattedBody>(() => MessageBodyFormatter.Format(message.Body));
    }

    public MessageSnapshot Message { get; }

    public long SequenceNumber => Message.SequenceNumber;

    public string MessageId => Message.MessageId ?? string.Empty;

    public string Subject => Message.Subject ?? string.Empty;

    public string EnqueuedText => FormatLocal(Message.EnqueuedTime);

    public string ScheduledText => FormatLocal(Message.ScheduledEnqueueTime);

    public int DeliveryCount => Message.DeliveryCount;

    public string SizeText => FormatSize(Message.Body.Length);

    public string DeadLetterReason => Message.DeadLetterReason ?? string.Empty;

    public bool HasDeadLetterReason => !string.IsNullOrEmpty(Message.DeadLetterReason);

    public string DeadLetterDescription => Message.DeadLetterErrorDescription ?? string.Empty;

    public string BodyText => body.Value.Text;

    public string BodySummary => body.Value.Kind switch
    {
        BodyKind.Empty => "Empty body",
        BodyKind.Json => $"JSON · {SizeText}",
        BodyKind.LegacyString => $"Text (legacy SDK string) · {SizeText}",
        BodyKind.Binary => $"Binary · {SizeText} · hex",
        _ => $"Text · {SizeText}"
    } + (string.IsNullOrEmpty(Message.ContentType) ? string.Empty : $" · {Message.ContentType}");

    public IReadOnlyList<PropertyRow> ApplicationProperties =>
        Message.ApplicationProperties
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PropertyRow(pair.Key, Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty))
            .ToList();

    public IReadOnlyList<PropertyRow> SystemProperties
    {
        get
        {
            var rows = new List<PropertyRow>
            {
                new("SequenceNumber", Message.SequenceNumber.ToString(CultureInfo.InvariantCulture)),
                new("MessageId", Message.MessageId ?? string.Empty),
                new("Subject", Message.Subject ?? string.Empty),
                new("ContentType", Message.ContentType ?? string.Empty),
                new("CorrelationId", Message.CorrelationId ?? string.Empty),
                new("SessionId", Message.SessionId ?? string.Empty),
                new("State", Message.State),
                new("EnqueuedTime", FormatUtc(Message.EnqueuedTime)),
                new("ScheduledEnqueueTime", FormatUtc(Message.ScheduledEnqueueTime)),
                new("DeliveryCount", Message.DeliveryCount.ToString(CultureInfo.InvariantCulture)),
                new("TimeToLive", Message.TimeToLive == TimeSpan.MaxValue ? "Never expires" : Message.TimeToLive.ToString()),
                new("ExpiresAt", Message.TimeToLive == TimeSpan.MaxValue ? string.Empty : FormatUtc(Message.ExpiresAt))
            };

            if (HasDeadLetterReason)
            {
                rows.Add(new("DeadLetterReason", Message.DeadLetterReason!));
                rows.Add(new("DeadLetterErrorDescription", Message.DeadLetterErrorDescription ?? string.Empty));
                rows.Add(new("DeadLetterSource", Message.DeadLetterSource ?? string.Empty));
            }

            return rows.Where(row => row.Value.Length > 0).ToList();
        }
    }

    static string FormatLocal(DateTimeOffset value) =>
        value == default ? string.Empty : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    static string FormatUtc(DateTimeOffset value) =>
        value == default ? string.Empty : value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture);

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
    };
}
