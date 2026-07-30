using System.Text;
using System.Text.Json;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal static class AcsEmailRequestParser
{
    internal static ParsedEmail Parse(
        Guid operationId,
        string apiVersion,
        string? clientRequestId,
        JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new EmailValidationException("InvalidRequest", "The request body must be a JSON object.");
        }

        var senderAddress = RequiredString(root, "senderAddress");
        var content = RequiredObject(root, "content");
        var subject = RequiredString(content, "subject");
        var plainText = OptionalString(content, "plainText");
        var html = OptionalString(content, "html");

        if (string.IsNullOrWhiteSpace(plainText) && string.IsNullOrWhiteSpace(html))
        {
            throw new EmailValidationException(
                "InvalidContent",
                "At least one of content.plainText or content.html is required.");
        }

        var recipientsObject = RequiredObject(root, "recipients");
        var recipients = new List<ParsedRecipient>();
        AddRecipients(recipientsObject, "to", RecipientKind.To, recipients);
        AddRecipients(recipientsObject, "cc", RecipientKind.Cc, recipients);
        AddRecipients(recipientsObject, "bcc", RecipientKind.Bcc, recipients);

        if (recipients.Count == 0)
        {
            throw new EmailValidationException(
                "InvalidRecipients",
                "At least one To, CC, or BCC recipient is required.");
        }

        AddRecipients(root, "replyTo", RecipientKind.ReplyTo, recipients);

        var headers = ParseHeaders(root);
        var attachments = ParseAttachments(root);
        bool? trackingDisabled = null;
        if (root.TryGetProperty("userEngagementTrackingDisabled", out var trackingProperty)
            && trackingProperty.ValueKind is not JsonValueKind.Null)
        {
            if (trackingProperty.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new EmailValidationException(
                    "InvalidTrackingSetting",
                    "userEngagementTrackingDisabled must be a boolean.");
            }

            trackingDisabled = trackingProperty.GetBoolean();
        }

        var rawJson = root.GetRawText();
        var searchText = BuildSearchText(
            operationId,
            senderAddress,
            subject,
            plainText,
            html,
            recipients);

        return new ParsedEmail(
            operationId,
            apiVersion,
            string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId,
            DateTimeOffset.UtcNow,
            senderAddress,
            subject,
            plainText,
            html,
            trackingDisabled,
            rawJson,
            searchText,
            recipients,
            headers,
            attachments);
    }

    private static Dictionary<string, string> ParseHeaders(JsonElement root)
    {
        if (!root.TryGetProperty("headers", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new EmailValidationException("InvalidHeaders", "headers must be a JSON object.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new EmailValidationException(
                    "InvalidHeaders",
                    $"The custom header '{property.Name}' must have a string value.");
            }

            result[property.Name] = property.Value.GetString()!;
        }

        return result;
    }

    private static List<ParsedAttachment> ParseAttachments(JsonElement root)
    {
        if (!root.TryGetProperty("attachments", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new EmailValidationException(
                "InvalidAttachments",
                "attachments must be a JSON array.");
        }

        var result = new List<ParsedAttachment>();
        var index = 0;
        foreach (var attachment in value.EnumerateArray())
        {
            if (attachment.ValueKind != JsonValueKind.Object)
            {
                throw new EmailValidationException(
                    "InvalidAttachment",
                    $"Attachment {index} must be a JSON object.");
            }

            var name = RequiredString(attachment, "name");
            var contentType = RequiredString(attachment, "contentType");
            var base64Content = RequiredString(attachment, "contentInBase64");
            byte[] content;
            try
            {
                content = Convert.FromBase64String(base64Content);
            }
            catch (FormatException)
            {
                throw new EmailValidationException(
                    "InvalidAttachment",
                    $"Attachment '{name}' does not contain valid base64 content.");
            }

            result.Add(
                new ParsedAttachment(
                    index,
                    name,
                    contentType,
                    OptionalString(attachment, "contentId"),
                    content));
            index++;
        }

        return result;
    }

    private static void AddRecipients(
        JsonElement parent,
        string propertyName,
        RecipientKind kind,
        List<ParsedRecipient> recipients)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new EmailValidationException(
                "InvalidRecipients",
                $"{propertyName} must be a JSON array.");
        }

        var ordinal = 0;
        foreach (var recipient in value.EnumerateArray())
        {
            if (recipient.ValueKind != JsonValueKind.Object)
            {
                throw new EmailValidationException(
                    "InvalidRecipients",
                    $"Each {propertyName} recipient must be a JSON object.");
            }

            recipients.Add(
                new ParsedRecipient(
                    kind,
                    ordinal,
                    RequiredString(recipient, "address"),
                    OptionalString(recipient, "displayName")));
            ordinal++;
        }
    }

    private static JsonElement RequiredObject(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new EmailValidationException(
                "InvalidRequest",
                $"{propertyName} is required and must be a JSON object.");
        }

        return value;
    }

    private static string RequiredString(JsonElement parent, string propertyName)
    {
        var value = OptionalString(parent, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new EmailValidationException(
                "InvalidRequest",
                $"{propertyName} is required and must be a non-empty string.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new EmailValidationException(
                "InvalidRequest",
                $"{propertyName} must be a string.");
        }

        return value.GetString();
    }

    private static string BuildSearchText(
        Guid operationId,
        string senderAddress,
        string subject,
        string? plainText,
        string? html,
        IEnumerable<ParsedRecipient> recipients)
    {
        var builder = new StringBuilder()
            .Append(operationId)
            .Append(' ')
            .Append(senderAddress)
            .Append(' ')
            .Append(subject)
            .Append(' ')
            .Append(plainText)
            .Append(' ')
            .Append(html);

        foreach (var recipient in recipients)
        {
            builder
                .Append(' ')
                .Append(recipient.Address)
                .Append(' ')
                .Append(recipient.DisplayName);
        }

        return builder.ToString();
    }
}

internal sealed class EmailValidationException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}
