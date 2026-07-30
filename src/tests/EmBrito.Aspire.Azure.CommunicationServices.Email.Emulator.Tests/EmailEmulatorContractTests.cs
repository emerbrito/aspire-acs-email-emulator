using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Communication.Email;
using Azure.Core.Pipeline;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator.Tests;

public sealed class EmailEmulatorContractTests
{
    [Fact]
    public async Task OfficialClientCompletesAndCapturesFullMessage()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);
        var message = CreateMessage("Complete message");
        message.Recipients.CC.Add(new EmailAddress("copy@example.test", "Copy Recipient"));
        message.Recipients.BCC.Add(new EmailAddress("blind@example.test", "Blind Recipient"));
        message.ReplyTo.Add(new EmailAddress("reply@example.test", "Reply Recipient"));
        message.Headers.Add("X-Correlation-ID", "emulator-contract-test");
        message.Attachments.Add(
            new EmailAttachment(
                "hello.txt",
                "text/plain",
                BinaryData.FromString("attachment body"))
            {
                ContentId = "inline-hello"
            });

        var operation = await client.SendAsync(WaitUntil.Completed, message);

        Assert.Equal(EmailSendStatus.Succeeded, operation.Value.Status);

        using var response = await httpClient.GetAsync(
            $"/_emulator/api/messages/{operation.Id}");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("Complete message", root.GetProperty("subject").GetString());
        Assert.Equal("donotreply@localhost", root.GetProperty("senderAddress").GetString());
        Assert.Equal(4, root.GetProperty("recipients").GetArrayLength());
        Assert.Equal("emulator-contract-test", root.GetProperty("headers")
            .GetProperty("X-Correlation-ID")
            .GetString());
        var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
        Assert.Equal("inline-hello", attachment.GetProperty("contentId").GetString());
    }

    [Fact]
    public async Task OperationIdIsIdempotentAndRejectsDifferentPayload()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);
        var operationId = Guid.NewGuid();

        await client.SendAsync(WaitUntil.Started, CreateMessage("First"), operationId);
        await client.SendAsync(WaitUntil.Started, CreateMessage("First"), operationId);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => client.SendAsync(
                WaitUntil.Started,
                CreateMessage("Different"),
                operationId));
        Assert.Equal(HttpStatusCode.Conflict, (HttpStatusCode)exception.Status);

        using var response = await httpClient.GetAsync("/_emulator/api/messages");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task ConcurrentMessagesAreCaptured()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);

        var operations = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(
                    index => client.SendAsync(
                        WaitUntil.Completed,
                        CreateMessage($"Concurrent message {index}"))));

        Assert.All(
            operations,
            operation => Assert.Equal(EmailSendStatus.Succeeded, operation.Value.Status));

        using var response = await httpClient.GetAsync("/_emulator/api/messages");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(20, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ConcurrentRetriesWithSameOperationIdAreIdempotent()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);
        var operationId = Guid.NewGuid();

        var operations = await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(
                    _ => client.SendAsync(
                        WaitUntil.Started,
                        CreateMessage("Concurrent retry"),
                        operationId)));

        Assert.All(operations, operation => Assert.Equal(operationId.ToString(), operation.Id));

        using var response = await httpClient.GetAsync("/_emulator/api/messages");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task DeletingInboxMessageDoesNotDeleteOperation()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);
        var operation = await client.SendAsync(WaitUntil.Started, CreateMessage("Delete me"));

        using var deleteResponse = await httpClient.DeleteAsync(
            $"/_emulator/api/messages/{operation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await operation.UpdateStatusAsync();

        Assert.True(operation.HasCompleted);
        Assert.Equal(EmailSendStatus.Succeeded, operation.Value.Status);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await httpClient.GetAsync($"/_emulator/api/messages/{operation.Id}")).StatusCode);
    }

    [Fact]
    public async Task InboxRendersCapturedMessageAndSandboxesHtml()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);
        var operation = await client.SendAsync(
            WaitUntil.Completed,
            CreateMessage("Visible in inbox"));

        var inbox = await httpClient.GetStringAsync("/");
        Assert.Contains("Visible in inbox", inbox, StringComparison.Ordinal);
        Assert.Contains(
            "Azure Communication Services Email Emulator",
            inbox,
            StringComparison.Ordinal);
        Assert.Contains("never delivered", inbox, StringComparison.Ordinal);
        Assert.Contains("class=\"inbox-pane\"", inbox, StringComparison.Ordinal);
        Assert.Contains("class=\"reading-pane\"", inbox, StringComparison.Ordinal);
        Assert.Contains(
            $"data-message-id=\"{operation.Id}\"",
            inbox,
            StringComparison.Ordinal);
        Assert.Contains(
            $"href=\"/messages/{operation.Id}/html\"",
            inbox,
            StringComparison.Ordinal);
        Assert.Contains("Open full size", inbox, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", inbox, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", inbox, StringComparison.Ordinal);

        using var htmlResponse = await httpClient.GetAsync($"/messages/{operation.Id}/html");
        htmlResponse.EnsureSuccessStatusCode();
        Assert.Contains(
            "default-src 'none'",
            htmlResponse.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedMessageRendersBesideTheInbox()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);
        var first = await client.SendAsync(
            WaitUntil.Completed,
            CreateMessage("First message"));
        var second = await client.SendAsync(
            WaitUntil.Completed,
            CreateMessage("Selected message"));

        var inbox = await httpClient.GetStringAsync($"/?message={second.Id}");

        Assert.Contains("First message", inbox, StringComparison.Ordinal);
        Assert.Contains("Selected message", inbox, StringComparison.Ordinal);
        Assert.Contains(
            $"data-message-detail-content data-message-id=\"{second.Id}\"",
            inbox,
            StringComparison.Ordinal);
        Assert.Contains(
            $"data-message-id=\"{second.Id}\" aria-current=\"true\"",
            inbox,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"data-message-detail-content data-message-id=\"{first.Id}\"",
            inbox,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventStreamPushesNewMessageNotification()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var eventClient = factory.CreateClient();
        using var sendClient = factory.CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_emulator/events");
        using var response = await eventClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);
        Assert.Equal("event: ready", await reader.ReadLineAsync(cancellation.Token));
        Assert.Equal("data: {}", await reader.ReadLineAsync(cancellation.Token));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(cancellation.Token));

        var client = CreateEmailClient(sendClient);
        var operation = await client.SendAsync(
            WaitUntil.Completed,
            CreateMessage("Live update"));

        Assert.Equal("event: inbox", await reader.ReadLineAsync(cancellation.Token));
        var data = Assert.IsType<string>(
            await reader.ReadLineAsync(cancellation.Token));
        Assert.StartsWith("data: ", data, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(data["data: ".Length..]);
        Assert.Equal(
            "message-created",
            document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(
            operation.Id,
            document.RootElement.GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task UiAssetsEnableFragmentNavigationAndLiveUpdates()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();

        var script = await httpClient.GetStringAsync("/assets/emulator.js");
        var styles = await httpClient.GetStringAsync("/assets/emulator.css");

        Assert.Contains("new EventSource", script, StringComparison.Ordinal);
        Assert.Contains("/_emulator/ui/messages/", script, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns", styles, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InboxSearchFiltersCapturedMessages()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        var client = CreateEmailClient(httpClient);
        await client.SendAsync(WaitUntil.Completed, CreateMessage("Matching message"));
        await client.SendAsync(WaitUntil.Completed, CreateMessage("Unrelated message"));

        var inbox = await httpClient.GetStringAsync("/?q=Matching");

        Assert.Contains("Matching message", inbox, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrelated message", inbox, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsUnsupportedApiVersion()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        using var content = new StringContent(
            """
            {
              "senderAddress": "sender@example.test",
              "content": { "subject": "Subject", "plainText": "Body" },
              "recipients": { "to": [{ "address": "recipient@example.test" }] }
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.PostAsync(
            "/emails:send?api-version=1900-01-01",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("UnsupportedApiVersion", response.Headers.GetValues("x-ms-error-code").Single());
    }

    [Fact]
    public async Task RejectsInvalidPayload()
    {
        await using var factory = new EmulatorApplicationFactory();
        using var httpClient = factory.CreateClient();
        using var content = new StringContent(
            """
            {
              "senderAddress": "sender@example.test",
              "recipients": { "to": [{ "address": "recipient@example.test" }] }
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.PostAsync(
            "/emails:send?api-version=2025-09-01",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", response.Headers.GetValues("x-ms-error-code").Single());
    }

    private static EmailClient CreateEmailClient(HttpClient httpClient)
    {
        var key = Convert.ToBase64String(Encoding.UTF8.GetBytes("local-emulator-key"));
        var options = new EmailClientOptions
        {
            Transport = new HttpClientTransport(httpClient)
        };
        return new EmailClient(
            $"Endpoint=http://localhost;AccessKey={key};SenderAddress=donotreply@localhost",
            options);
    }

    private static EmailMessage CreateMessage(string subject) =>
        new(
            "donotreply@localhost",
            new EmailRecipients(
                [new EmailAddress("person@example.test", "Test Recipient")]),
            new EmailContent(subject)
            {
                PlainText = "Plain-text body",
                Html = "<strong>HTML body</strong>"
            });

    private sealed class EmulatorApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(),
            "embrito-acs-email-emulator-tests",
            $"{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Emulator:DatabasePath", _databasePath);
        }
    }
}
