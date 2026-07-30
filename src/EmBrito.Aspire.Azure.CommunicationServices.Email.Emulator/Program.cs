using EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

const long maximumRequestSize = 10 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maximumRequestSize;
});
builder.Services.AddSingleton<EmailStore>();
builder.Services.AddSingleton<EmailEmulatorEventHub>();

var app = builder.Build();

var store = app.Services.GetRequiredService<EmailStore>();
await store.InitializeAsync();

app.UseStaticFiles();

app.MapGet("/", InboxUi.RenderInboxAsync);
app.MapGet("/messages/{operationId}", InboxUi.RedirectToMessage);
app.MapGet("/messages/{operationId}/html", InboxUi.RenderHtmlBodyAsync);
app.MapGet("/messages/{operationId}/attachments/{attachmentIndex:int}", InboxUi.DownloadAttachmentAsync);
app.MapPost("/messages/{operationId}/delete", InboxUi.DeleteMessageAsync);
app.MapPost("/messages/delete-all", InboxUi.DeleteAllMessagesAsync);

app.MapGet("/_emulator/ui/inbox", InboxUi.RenderMessageListAsync);
app.MapGet("/_emulator/ui/messages/{operationId}", InboxUi.RenderMessageFragmentAsync);
app.MapGet("/_emulator/events", EmulatorEventsApi.StreamAsync);

app.MapPost("/emails:send", AcsEmailApi.SendAsync);
app.MapGet("/emails/operations/{operationId}", AcsEmailApi.GetOperationAsync);

app.MapGet("/_emulator/api/messages", EmulatorAdminApi.ListMessagesAsync);
app.MapGet("/_emulator/api/messages/{operationId}", EmulatorAdminApi.GetMessageAsync);
app.MapDelete("/_emulator/api/messages/{operationId}", EmulatorAdminApi.DeleteMessageAsync);
app.MapDelete("/_emulator/api/messages", EmulatorAdminApi.DeleteAllMessagesAsync);

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/livez", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program;
