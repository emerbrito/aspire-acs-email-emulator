namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal enum RecipientKind
{
    To,
    Cc,
    Bcc,
    ReplyTo
}

internal sealed record ParsedRecipient(
    RecipientKind Kind,
    int Ordinal,
    string Address,
    string? DisplayName);

internal sealed record ParsedAttachment(
    int Index,
    string Name,
    string ContentType,
    string? ContentId,
    byte[] Content);

internal sealed record ParsedEmail(
    Guid OperationId,
    string ApiVersion,
    string? ClientRequestId,
    DateTimeOffset CapturedAt,
    string SenderAddress,
    string Subject,
    string? PlainText,
    string? Html,
    bool? UserEngagementTrackingDisabled,
    string RawJson,
    string SearchText,
    IReadOnlyList<ParsedRecipient> Recipients,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<ParsedAttachment> Attachments);

internal sealed record EmailSummary(
    Guid OperationId,
    DateTimeOffset CapturedAt,
    string SenderAddress,
    string Subject,
    string Recipients);

internal sealed record StoredEmail(
    Guid OperationId,
    string ApiVersion,
    string? ClientRequestId,
    DateTimeOffset CapturedAt,
    string SenderAddress,
    string Subject,
    string? PlainText,
    string? Html,
    bool? UserEngagementTrackingDisabled,
    string RawJson,
    IReadOnlyList<ParsedRecipient> Recipients,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<StoredAttachment> Attachments);

internal sealed record StoredAttachment(
    int Index,
    string Name,
    string ContentType,
    string? ContentId,
    long Length);

internal sealed record AttachmentContent(
    string Name,
    string ContentType,
    byte[] Content);

internal enum CaptureResult
{
    Created,
    Duplicate,
    Conflict
}
