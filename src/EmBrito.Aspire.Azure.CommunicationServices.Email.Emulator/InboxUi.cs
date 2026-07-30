using System.Globalization;
using System.Net;
using System.Text;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal static class InboxUi
{
    private const string ProductName = "Azure Communication Services Email Emulator";

    internal static async Task<IResult> RenderInboxAsync(
        string? q,
        string? message,
        EmailStore store,
        CancellationToken cancellationToken)
    {
        var messages = await store.SearchAsync(q, cancellationToken).ConfigureAwait(false);
        var selectedId = ParseOperationId(message);
        StoredEmail? selectedMessage = null;

        if (selectedId is not null)
        {
            selectedMessage = await store
                .GetAsync(selectedId.Value, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (selectedMessage is null && messages is [var first, ..])
        {
            selectedId = first.OperationId;
            selectedMessage = await store
                .GetAsync(first.OperationId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var body =
            $$"""
            <header class="app-bar">
              <div class="brand-mark" aria-hidden="true">
                <span></span>
              </div>
              <div class="brand-copy">
                <p class="eyebrow">Local development</p>
                <h1>{{ProductName}}</h1>
                <p>Messages are captured locally and are never delivered.</p>
              </div>
              <div class="live-status" data-live-status data-state="connecting" role="status">
                <span aria-hidden="true"></span>
                <strong>Connecting</strong>
              </div>
            </header>
            <main
              class="mail-workspace"
              data-mail-workspace
              data-selected-id="{{selectedId?.ToString("D") ?? string.Empty}}">
              <aside class="inbox-pane" aria-label="Captured email">
                <form class="search" method="get" action="/" data-search-form>
                  <label for="q">Search inbox</label>
                  <div>
                    <svg aria-hidden="true" viewBox="0 0 20 20">
                      <path d="m17 17-3.7-3.7m1.7-4.8a6.5 6.5 0 1 1-13 0 6.5 6.5 0 0 1 13 0Z"></path>
                    </svg>
                    <input
                      id="q"
                      name="q"
                      value="{{Encode(q ?? string.Empty)}}"
                      placeholder="Sender, recipient, subject, or content"
                      autocomplete="off">
                    <button type="submit">Search</button>
                  </div>
                </form>
                <div class="inbox-results" data-inbox-results aria-live="polite">
                  {{RenderMessageList(messages, q, selectedId)}}
                </div>
              </aside>
              <section
                class="reading-pane"
                data-message-detail
                aria-label="Selected email"
                aria-live="polite">
                {{(selectedMessage is null ? RenderEmptyDetail() : RenderMessageDetail(selectedMessage))}}
              </section>
            </main>
            <template id="empty-message-template">
              {{RenderEmptyDetail()}}
            </template>
            """;

        return Html(Page(body));
    }

    internal static async Task<IResult> RenderMessageListAsync(
        string? q,
        string? selected,
        EmailStore store,
        CancellationToken cancellationToken)
    {
        var messages = await store.SearchAsync(q, cancellationToken).ConfigureAwait(false);
        return Html(RenderMessageList(messages, q, ParseOperationId(selected)));
    }

    internal static async Task<IResult> RenderMessageFragmentAsync(
        string operationId,
        EmailStore store,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationId, out var parsedOperationId))
        {
            return Results.NotFound();
        }

        var message = await store
            .GetAsync(parsedOperationId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return message is null
            ? Results.NotFound()
            : Html(RenderMessageDetail(message));
    }

    internal static IResult RedirectToMessage(string operationId) =>
        Guid.TryParse(operationId, out var parsedOperationId)
            ? Results.Redirect($"/?message={parsedOperationId:D}")
            : Results.NotFound();

    internal static async Task<IResult> RenderHtmlBodyAsync(
        string operationId,
        HttpResponse response,
        EmailStore store,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationId, out var parsedOperationId))
        {
            return Results.NotFound();
        }

        var message = await store.GetAsync(parsedOperationId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (message?.Html is null)
        {
            return Results.NotFound();
        }

        response.Headers.ContentSecurityPolicy =
            "default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Content(message.Html, "text/html; charset=utf-8");
    }

    internal static async Task<IResult> DownloadAttachmentAsync(
        string operationId,
        int attachmentIndex,
        HttpResponse response,
        EmailStore store,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationId, out var parsedOperationId))
        {
            return Results.NotFound();
        }

        var attachment = await store
            .GetAttachmentAsync(parsedOperationId, attachmentIndex, cancellationToken)
            .ConfigureAwait(false);
        if (attachment is null)
        {
            return Results.NotFound();
        }

        response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(
            attachment.Content,
            "application/octet-stream",
            SanitizeFileName(attachment.Name));
    }

    internal static async Task<IResult> DeleteMessageAsync(
        string operationId,
        EmailStore store,
        EmailEmulatorEventHub eventHub,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(operationId, out var parsedOperationId)
            && await store.DeleteAsync(parsedOperationId, cancellationToken).ConfigureAwait(false))
        {
            eventHub.Publish(EmailEmulatorEvent.MessageDeleted(parsedOperationId));
        }

        return Results.Redirect("/");
    }

    internal static async Task<IResult> DeleteAllMessagesAsync(
        EmailStore store,
        EmailEmulatorEventHub eventHub,
        CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAllAsync(cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            eventHub.Publish(EmailEmulatorEvent.AllMessagesDeleted(deleted));
        }

        return Results.Redirect("/");
    }

    private static string RenderMessageList(
        IReadOnlyList<EmailSummary> messages,
        string? query,
        Guid? selectedId)
    {
        var content = new StringBuilder(
            $$"""
            <div class="inbox-toolbar">
              <div>
                <strong>Inbox</strong>
                <span data-message-count>{{messages.Count}}</span>
              </div>
              <form method="post" action="/messages/delete-all" data-delete-all>
                <button
                  class="icon-button danger"
                  type="submit"
                  title="Delete all messages"
                  aria-label="Delete all messages"
                  {{(messages.Count == 0 ? "disabled" : string.Empty)}}>
                  <svg aria-hidden="true" viewBox="0 0 20 20">
                    <path d="M3 5h14M8 2h4l1 3H7l1-3Zm-2 3 1 12h6l1-12M9 8v6m2-6v6"></path>
                  </svg>
                </button>
              </form>
            </div>
            """);

        if (messages.Count == 0)
        {
            content.Append(
                CultureInfo.InvariantCulture,
                $$"""
                <section class="empty-inbox">
                  <div class="empty-icon" aria-hidden="true">
                    <svg viewBox="0 0 24 24">
                      <path d="M3 6.5 12 13l9-6.5M5 19h14a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2Z"></path>
                    </svg>
                  </div>
                  <h2>{{(string.IsNullOrWhiteSpace(query) ? "No captured email" : "No matching email")}}</h2>
                  <p>{{(string.IsNullOrWhiteSpace(query)
                      ? "New messages appear here automatically."
                      : "Try a different sender, recipient, subject, or phrase.")}}</p>
                </section>
                """);
            return content.ToString();
        }

        content.Append("<div class=\"message-list\" role=\"list\">");
        foreach (var message in messages)
        {
            var isSelected = message.OperationId == selectedId;
            var href = BuildMessageUrl(message.OperationId, query);
            content
                .Append("<a class=\"message-row")
                .Append(isSelected ? " selected" : string.Empty)
                .Append("\" role=\"listitem\" href=\"")
                .Append(Encode(href))
                .Append("\" data-message-link data-message-id=\"")
                .Append(message.OperationId.ToString("D"))
                .Append('"')
                .Append(isSelected ? " aria-current=\"true\"" : string.Empty)
                .Append("><span class=\"message-row-top\"><strong>")
                .Append(Encode(message.Subject))
                .Append("</strong><time datetime=\"")
                .Append(message.CapturedAt.ToString("O", CultureInfo.InvariantCulture))
                .Append("\">")
                .Append(Encode(FormatMessageTime(message.CapturedAt)))
                .Append("</time></span><span class=\"message-sender\">")
                .Append(Encode(message.SenderAddress))
                .Append("</span><span class=\"message-recipients\">To ")
                .Append(Encode(message.Recipients))
                .Append("</span></a>");
        }

        content.Append("</div>");
        return content.ToString();
    }

    private static string RenderMessageDetail(StoredEmail message)
    {
        var recipients = new StringBuilder();
        AppendRecipientRow(recipients, "To", message.Recipients, RecipientKind.To);
        AppendRecipientRow(recipients, "CC", message.Recipients, RecipientKind.Cc);
        AppendRecipientRow(recipients, "BCC", message.Recipients, RecipientKind.Bcc);
        AppendRecipientRow(recipients, "Reply to", message.Recipients, RecipientKind.ReplyTo);

        var headers = new StringBuilder();
        foreach (var header in message.Headers)
        {
            headers
                .Append("<dt>")
                .Append(Encode(header.Key))
                .Append("</dt><dd>")
                .Append(Encode(header.Value))
                .Append("</dd>");
        }

        var attachments = new StringBuilder();
        foreach (var attachment in message.Attachments)
        {
            attachments
                .Append("<li><a href=\"/messages/")
                .Append(message.OperationId.ToString("D"))
                .Append("/attachments/")
                .Append(attachment.Index.ToString(CultureInfo.InvariantCulture))
                .Append("\"><svg aria-hidden=\"true\" viewBox=\"0 0 20 20\"><path d=\"m7 10.5 4.8-4.8a2.5 2.5 0 0 1 3.5 3.6l-6.6 6.5a4 4 0 0 1-5.6-5.6l6.2-6.3\"></path></svg><span><strong>")
                .Append(Encode(attachment.Name))
                .Append("</strong><small>")
                .Append(Encode(attachment.ContentType))
                .Append(" · ")
                .Append(FormatSize(attachment.Length))
                .Append(attachment.ContentId is null
                    ? string.Empty
                    : $" · CID {Encode(attachment.ContentId)}")
                .Append("</small></span></a></li>");
        }

        return
            $$"""
            <article class="message-detail" data-message-detail-content data-message-id="{{message.OperationId:D}}">
              <header class="message-header">
                <div>
                  <p class="message-date">Captured {{Encode(message.CapturedAt.LocalDateTime.ToString("f", CultureInfo.CurrentCulture))}}</p>
                  <h2>{{Encode(message.Subject)}}</h2>
                </div>
                <form method="post" action="/messages/{{message.OperationId:D}}/delete" data-delete-message>
                  <button class="button danger" type="submit">
                    <svg aria-hidden="true" viewBox="0 0 20 20">
                      <path d="M3 5h14M8 2h4l1 3H7l1-3Zm-2 3 1 12h6l1-12M9 8v6m2-6v6"></path>
                    </svg>
                    Delete
                  </button>
                </form>
              </header>
              <dl class="metadata primary-metadata">
                <dt>From</dt><dd>{{Encode(message.SenderAddress)}}</dd>
                {{recipients}}
              </dl>
              <div class="body-grid">
                <section class="panel">
                  <div class="panel-heading">
                    <h3>Plain text</h3>
                  </div>
                  <pre>{{Encode(message.PlainText ?? "No plain-text body.")}}</pre>
                </section>
                <section class="panel">
                  <div class="panel-heading">
                    <h3>HTML preview</h3>
                    <div class="panel-actions">
                      <span>Sandboxed</span>
                      {{(message.Html is null
                          ? string.Empty
                          : $"""
                            <a
                              class="preview-link"
                              href="/messages/{message.OperationId:D}/html"
                              target="_blank"
                              rel="noopener"
                              aria-label="Open HTML preview full size in a new tab">
                              Open full size
                              <svg aria-hidden="true" viewBox="0 0 20 20">
                                <path d="M11 3h6v6m0-6-8 8M8 5H4a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1v-4"></path>
                              </svg>
                            </a>
                            """)}}
                    </div>
                  </div>
                  {{(message.Html is null
                      ? "<p class=\"muted\">No HTML body.</p>"
                      : $"<iframe title=\"HTML email body\" sandbox src=\"/messages/{message.OperationId:D}/html\"></iframe>")}}
                </section>
              </div>
              <section class="panel supplemental">
                <div class="panel-heading">
                  <h3>Attachments</h3>
                  <span>{{message.Attachments.Count}}</span>
                </div>
                {{(message.Attachments.Count == 0
                    ? "<p class=\"muted\">No attachments.</p>"
                    : $"<ul class=\"attachments\">{attachments}</ul>")}}
              </section>
              <details class="technical-details">
                <summary>Technical details</summary>
                <dl class="metadata compact">
                  <dt>Operation ID</dt><dd><code>{{message.OperationId:D}}</code></dd>
                  <dt>API version</dt><dd><code>{{Encode(message.ApiVersion)}}</code></dd>
                  {{(message.ClientRequestId is null
                      ? string.Empty
                      : $"<dt>Client request ID</dt><dd><code>{Encode(message.ClientRequestId)}</code></dd>")}}
                  <dt>Tracking disabled</dt><dd>{{Encode(message.UserEngagementTrackingDisabled?.ToString() ?? "Not specified")}}</dd>
                  {{(message.Headers.Count == 0
                      ? string.Empty
                      : $"<dt class=\"metadata-section\">Custom headers</dt><dd></dd>{headers}")}}
                </dl>
              </details>
            </article>
            """;
    }

    private static string RenderEmptyDetail() =>
        """
        <section class="empty-detail" data-empty-detail>
          <div class="empty-detail-art" aria-hidden="true">
            <svg viewBox="0 0 64 64">
              <path d="M9 18h46v31H9z"></path>
              <path d="m10 20 22 17 22-17M10 48l16-16m28 16L38 32"></path>
            </svg>
          </div>
          <h2>Select an email</h2>
          <p>Choose a captured message from the inbox to inspect its recipients, content, headers, and attachments.</p>
        </section>
        """;

    private static string Page(string body) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta
            http-equiv="Content-Security-Policy"
            content="default-src 'self'; connect-src 'self'; frame-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; base-uri 'none'; form-action 'self'">
          <title>{{ProductName}}</title>
          <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='12' fill='%230078d4'/%3E%3Cpath d='M12 20h40v27H12zM12 21l20 16 20-16' fill='none' stroke='white' stroke-width='4' stroke-linejoin='round'/%3E%3C/svg%3E">
          <link rel="stylesheet" href="/assets/emulator.css">
          <script src="/assets/emulator.js" defer></script>
        </head>
        <body>
          {{body}}
        </body>
        </html>
        """;

    private static void AppendRecipientRow(
        StringBuilder builder,
        string label,
        IEnumerable<ParsedRecipient> recipients,
        RecipientKind kind)
    {
        var values = recipients
            .Where(recipient => recipient.Kind == kind)
            .Select(
                recipient => string.IsNullOrWhiteSpace(recipient.DisplayName)
                    ? recipient.Address
                    : $"{recipient.DisplayName} <{recipient.Address}>")
            .ToArray();
        if (values.Length == 0)
        {
            return;
        }

        builder
            .Append("<dt>")
            .Append(Encode(label))
            .Append("</dt><dd>")
            .Append(Encode(string.Join(", ", values)))
            .Append("</dd>");
    }

    private static Guid? ParseOperationId(string? value) =>
        Guid.TryParse(value, out var operationId) ? operationId : null;

    private static string BuildMessageUrl(Guid operationId, string? query)
    {
        var url = $"/?message={operationId:D}";
        return string.IsNullOrWhiteSpace(query)
            ? url
            : $"{url}&q={Uri.EscapeDataString(query)}";
    }

    private static string FormatMessageTime(DateTimeOffset value)
    {
        var local = value.LocalDateTime;
        return local.Date == DateTime.Today
            ? local.ToString("t", CultureInfo.CurrentCulture)
            : local.ToString("MMM d", CultureInfo.CurrentCulture);
    }

    private static IResult Html(string content) =>
        Results.Content(content, "text/html; charset=utf-8");

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "attachment" : sanitized;
    }

    private static string FormatSize(long value) =>
        value switch
        {
            < 1024 => $"{value} B",
            < 1024 * 1024 => $"{value / 1024d:F1} KB",
            _ => $"{value / (1024d * 1024d):F1} MB"
        };
}
