using System.Globalization;
using System.Text.Json;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal static class AcsEmailApi
{
    private static readonly HashSet<string> SupportedApiVersions =
    [
        "2023-03-31",
        "2024-07-01-preview",
        "2025-09-01"
    ];

    internal static async Task<IResult> SendAsync(
        HttpRequest request,
        HttpResponse response,
        EmailStore store,
        EmailEmulatorEventHub eventHub,
        CancellationToken cancellationToken)
    {
        var apiVersion = request.Query["api-version"].ToString();
        if (!SupportedApiVersions.Contains(apiVersion))
        {
            return Error(
                response,
                StatusCodes.Status400BadRequest,
                "UnsupportedApiVersion",
                $"The API version '{apiVersion}' is not supported by this emulator.");
        }

        Guid operationId;
        var suppliedOperationId = request.Headers["Operation-Id"].ToString();
        if (string.IsNullOrWhiteSpace(suppliedOperationId))
        {
            operationId = Guid.NewGuid();
        }
        else if (!Guid.TryParse(suppliedOperationId, out operationId))
        {
            return Error(
                response,
                StatusCodes.Status400BadRequest,
                "InvalidOperationId",
                "The Operation-Id header must be a UUID.");
        }

        ParsedEmail parsedEmail;
        try
        {
            using var document = await JsonDocument
                .ParseAsync(request.Body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            parsedEmail = AcsEmailRequestParser.Parse(
                operationId,
                apiVersion,
                request.Headers["x-ms-client-request-id"].ToString(),
                document.RootElement);
        }
        catch (JsonException exception)
        {
            return Error(
                response,
                StatusCodes.Status400BadRequest,
                "InvalidRequest",
                exception.Message);
        }
        catch (EmailValidationException exception)
        {
            return Error(
                response,
                StatusCodes.Status400BadRequest,
                exception.Code,
                exception.Message);
        }

        var result = await store
            .CaptureAsync(parsedEmail, cancellationToken)
            .ConfigureAwait(false);

        if (result == CaptureResult.Conflict)
        {
            return Error(
                response,
                StatusCodes.Status409Conflict,
                "OperationIdConflict",
                "The Operation-Id has already been used for a different email request.");
        }

        if (result == CaptureResult.Created)
        {
            eventHub.Publish(EmailEmulatorEvent.MessageCreated(operationId));
        }

        var operationLocation =
            $"{request.Scheme}://{request.Host}{request.PathBase}" +
            $"/emails/operations/{operationId:D}?api-version={Uri.EscapeDataString(apiVersion)}";
        response.Headers.Location = operationLocation;
        response.Headers["Operation-Location"] = operationLocation;

        return Results.Json(
            new
            {
                id = operationId.ToString("D", CultureInfo.InvariantCulture),
                status = "Running"
            },
            statusCode: StatusCodes.Status202Accepted);
    }

    internal static async Task<IResult> GetOperationAsync(
        string operationId,
        HttpRequest request,
        HttpResponse response,
        EmailStore store,
        CancellationToken cancellationToken)
    {
        var apiVersion = request.Query["api-version"].ToString();
        if (!SupportedApiVersions.Contains(apiVersion))
        {
            return Error(
                response,
                StatusCodes.Status400BadRequest,
                "UnsupportedApiVersion",
                $"The API version '{apiVersion}' is not supported by this emulator.");
        }

        if (!Guid.TryParse(operationId, out var parsedOperationId))
        {
            return Error(
                response,
                StatusCodes.Status400BadRequest,
                "InvalidOperationId",
                "The operation ID must be a UUID.");
        }

        if (!await store.OperationExistsAsync(parsedOperationId, cancellationToken).ConfigureAwait(false))
        {
            return Error(
                response,
                StatusCodes.Status404NotFound,
                "OperationNotFound",
                $"No email send operation with ID '{operationId}' was found.");
        }

        return Results.Json(new
        {
            id = parsedOperationId.ToString("D", CultureInfo.InvariantCulture),
            status = "Succeeded"
        });
    }

    private static IResult Error(
        HttpResponse response,
        int statusCode,
        string code,
        string message)
    {
        response.Headers["x-ms-error-code"] = code;
        return Results.Json(
            new
            {
                error = new
                {
                    code,
                    message
                }
            },
            statusCode: statusCode);
    }
}
