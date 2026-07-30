namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal static class EmulatorAdminApi
{
    internal static async Task<IResult> ListMessagesAsync(
        string? q,
        EmailStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.SearchAsync(q, cancellationToken).ConfigureAwait(false));

    internal static async Task<IResult> GetMessageAsync(
        string operationId,
        EmailStore store,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationId, out var parsedOperationId))
        {
            return Results.BadRequest(new { error = "The operation ID must be a UUID." });
        }

        var message = await store.GetAsync(parsedOperationId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return message is null ? Results.NotFound() : Results.Ok(message);
    }

    internal static async Task<IResult> DeleteMessageAsync(
        string operationId,
        EmailStore store,
        EmailEmulatorEventHub eventHub,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationId, out var parsedOperationId))
        {
            return Results.BadRequest(new { error = "The operation ID must be a UUID." });
        }

        if (!await store.DeleteAsync(parsedOperationId, cancellationToken).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        eventHub.Publish(EmailEmulatorEvent.MessageDeleted(parsedOperationId));
        return Results.NoContent();
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

        return Results.Ok(new { deleted });
    }
}
