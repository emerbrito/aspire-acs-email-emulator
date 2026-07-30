using System.Text.Json;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal static class EmulatorEventsApi
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    internal static async Task StreamAsync(
        HttpContext context,
        EmailEmulatorEventHub eventHub,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = eventHub.Subscribe();

        await context.Response
            .WriteAsync("event: ready\ndata: {}\n\n", cancellationToken)
            .ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await foreach (var notification in subscription.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(notification, SerializerOptions);
                await context.Response
                    .WriteAsync($"event: inbox\ndata: {json}\n\n", cancellationToken)
                    .ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing a browser tab aborts the long-lived event stream.
        }
    }
}
