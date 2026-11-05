using System.Runtime.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace ServiceBusExplorer.Desktop.Services;

public enum BodyKind
{
    Empty,
    Json,
    Text,
    LegacyString,
    Binary
}

public sealed record FormattedBody(BodyKind Kind, string Text);

public static class MessageBodyFormatter
{
    const int BinaryPreviewBytes = 4096;

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static FormattedBody Format(byte[] body)
    {
        if (body.Length == 0)
        {
            return new FormattedBody(BodyKind.Empty, string.Empty);
        }

        // Messages sent as a string by the legacy WindowsAzure.ServiceBus SDK (which this app's
        // WinForms side still uses) arrive wrapped in DataContract binary XML.
        if (TryReadLegacyString(body, out var legacyText))
        {
            return FormatText(legacyText, BodyKind.LegacyString);
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(body).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return new FormattedBody(BodyKind.Binary, FormatBinary(body));
        }

        // Bytes such as 00 01 02 are valid UTF-8 but aren't text.
        if (text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')))
        {
            return new FormattedBody(BodyKind.Binary, FormatBinary(body));
        }

        return FormatText(text, BodyKind.Text);
    }

    static FormattedBody FormatText(string text, BodyKind kindIfNotJson)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return new FormattedBody(BodyKind.Json, JsonSerializer.Serialize(document.RootElement, IndentedJson));
            }
            catch (JsonException)
            {
                // Not JSON after all; show it as text.
            }
        }

        return new FormattedBody(kindIfNotJson, text);
    }

    static bool TryReadLegacyString(byte[] body, out string text)
    {
        text = string.Empty;

        // DataContract binary XML for a string starts with a short element record (0x40) named "string".
        if (body.Length < 8 || body[0] != 0x40 || body[1] != 0x06 || Encoding.ASCII.GetString(body, 2, 6) != "string")
        {
            return false;
        }

        try
        {
            using var reader = XmlDictionaryReader.CreateBinaryReader(body, XmlDictionaryReaderQuotas.Max);
            text = (string?)new DataContractSerializer(typeof(string)).ReadObject(reader) ?? string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or SerializationException)
        {
            return false;
        }
    }

    static string FormatBinary(byte[] body)
    {
        var shown = Math.Min(body.Length, BinaryPreviewBytes);
        var builder = new StringBuilder();

        for (var offset = 0; offset < shown; offset += 16)
        {
            var count = Math.Min(16, shown - offset);
            builder.Append(offset.ToString("X8")).Append("  ");
            builder.Append(Convert.ToHexString(body, offset, count).ToLowerInvariant().PadRight(32));
            builder.AppendLine();
        }

        if (body.Length > shown)
        {
            builder.AppendLine($"... {body.Length - shown:N0} more bytes");
        }

        return builder.ToString();
    }
}
