using System.Text.Json;

namespace WinAgentNotification.Core;

public static class MessageParser
{
    public static ParseResult Parse(ReadOnlyMemory<byte> payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return ParseResult.Fail($"invalid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ParseResult.Fail("payload is not a JSON object");

            if (!root.TryGetProperty("title", out var titleElement)
                || titleElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(titleElement.GetString()))
            {
                return ParseResult.Fail("missing required field 'title'");
            }

            var title = titleElement.GetString()!;

            var body = root.TryGetProperty("body", out var bodyElement)
                       && bodyElement.ValueKind == JsonValueKind.String
                ? bodyElement.GetString()!
                : string.Empty;

            var level = NotificationLevel.Info;
            string? warning = null;
            if (root.TryGetProperty("level", out var levelElement)
                && levelElement.ValueKind == JsonValueKind.String)
            {
                var raw = levelElement.GetString()!;
                if (Enum.TryParse<NotificationLevel>(raw, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed)
                    && !char.IsDigit(raw[0]))
                {
                    level = parsed;
                }
                else
                {
                    warning = $"unknown level '{raw}', treated as info";
                }
            }

            return ParseResult.Ok(new NotificationMessage(title, body, level), warning);
        }
    }
}
