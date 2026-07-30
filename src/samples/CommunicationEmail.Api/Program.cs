using Azure;
using EmBrito.Aspire.Azure.CommunicationServices.Email;
using Azure.Communication.Email;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureCommunicationEmailClient("email");

var app = builder.Build();

app.MapGet(
    "/",
    (AzureCommunicationEmailSettings settings) =>
        Results.Content(BuildEmailForm(settings.SenderAddress!), "text/html"));

app.MapPost(
    "/email",
    async (
        SendEmailRequest request,
        EmailClient client,
        AzureCommunicationEmailSettings settings,
        CancellationToken cancellationToken) =>
    {
        if (!System.Net.Mail.MailAddress.TryCreate(request.To, out _))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(request.To)] = ["A valid recipient email address is required."]
                });
        }

        var content = new EmailContent(request.Subject)
        {
            PlainText = request.Text,
            Html = request.Html
        };
        var message = new EmailMessage(settings.SenderAddress!, request.To, content);
        var operation = await client.SendAsync(
            WaitUntil.Completed,
            message,
            cancellationToken);

        return Results.Ok(
            new
            {
                id = operation.Id,
                status = operation.Value.Status.ToString()
            });
    });

app.MapDefaultEndpoints();

app.Run();

static string BuildEmailForm(string senderAddress) =>
    """
    <!doctype html>
    <html lang="en">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>Communication Email</title>
      <style>
        :root {
          color-scheme: light dark;
          font-family: "Segoe UI", system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
          line-height: 1.5;
          background: #f7f9fb;
          color: #1f2937;
        }

        body {
          margin: 0;
          min-height: 100vh;
          display: grid;
          place-items: center;
          padding: 32px 16px;
        }

        main {
          width: min(720px, 100%);
        }

        h1 {
          margin: 0 0 8px;
          font-size: clamp(1.75rem, 4vw, 2.4rem);
          line-height: 1.1;
        }

        p {
          margin: 0 0 24px;
          color: #4b5563;
        }

        form {
          display: grid;
          gap: 16px;
          padding: 24px;
          border: 1px solid #d9e2ec;
          border-radius: 8px;
          background: #ffffff;
          box-shadow: 0 16px 40px rgb(15 23 42 / 8%);
        }

        label {
          display: grid;
          gap: 6px;
          font-weight: 600;
        }

        input,
        textarea {
          width: 100%;
          box-sizing: border-box;
          border: 1px solid #c8d3df;
          border-radius: 6px;
          padding: 10px 12px;
          font: inherit;
          color: inherit;
          background: #ffffff;
        }

        textarea {
          min-height: 108px;
          resize: vertical;
        }

        button {
          justify-self: start;
          border: 0;
          border-radius: 6px;
          padding: 10px 16px;
          font: inherit;
          font-weight: 700;
          color: #ffffff;
          background: #2563eb;
          cursor: pointer;
        }

        button:disabled {
          cursor: progress;
          opacity: .7;
        }

        output {
          display: none;
          white-space: pre-wrap;
          border-radius: 6px;
          padding: 12px;
          border: 1px solid #c8d3df;
          background: #f8fafc;
        }

        output[data-state="ok"] {
          display: block;
          border-color: #8fd19e;
          background: #f0fff4;
        }

        output[data-state="error"] {
          display: block;
          border-color: #f0a0a0;
          background: #fff5f5;
        }

        .sender {
          font-family: Consolas, "Cascadia Mono", monospace;
          overflow-wrap: anywhere;
        }

        @media (prefers-color-scheme: dark) {
          :root {
            background: #111827;
            color: #e5e7eb;
          }

          p {
            color: #b6c2d1;
          }

          form,
          input,
          textarea {
            background: #1f2937;
            border-color: #374151;
          }

          output {
            background: #111827;
            border-color: #374151;
          }

          output[data-state="ok"] {
            background: #102718;
            border-color: #23804b;
          }

          output[data-state="error"] {
            background: #2d1518;
            border-color: #b45353;
          }
        }
      </style>
    </head>
    <body>
      <main>
        <h1>Send an email</h1>
        <p>From <span class="sender">__SENDER_ADDRESS__</span>. Local runs deliver to the ACS Email emulator inbox.</p>
        <form id="email-form">
          <label>
            To
            <input name="to" type="email" value="developer@example.test" autocomplete="email" required>
          </label>
          <label>
            Subject
            <input name="subject" value="Hello from Aspire" required>
          </label>
          <label>
            Plain text
            <textarea name="text">Plain-text body</textarea>
          </label>
          <label>
            HTML
            <textarea name="html">&lt;strong&gt;HTML body&lt;/strong&gt;</textarea>
          </label>
          <button type="submit">Send email</button>
          <output id="result" role="status" aria-live="polite"></output>
        </form>
      </main>
      <script>
        const form = document.querySelector("#email-form");
        const button = form.querySelector("button");
        const result = document.querySelector("#result");

        form.addEventListener("submit", async event => {
          event.preventDefault();
          button.disabled = true;
          result.dataset.state = "";
          result.textContent = "Sending...";

          const data = Object.fromEntries(new FormData(form).entries());

          try {
            const response = await fetch("/email", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify(data)
            });

            const payload = await response.json();

            if (!response.ok) {
              throw new Error(JSON.stringify(payload, null, 2));
            }

            result.dataset.state = "ok";
            result.textContent = `Sent\n\n${JSON.stringify(payload, null, 2)}`;
          } catch (error) {
            result.dataset.state = "error";
            result.textContent = error instanceof Error ? error.message : String(error);
          } finally {
            button.disabled = false;
          }
        });
      </script>
    </body>
    </html>
    """.Replace("__SENDER_ADDRESS__", WebUtility.HtmlEncode(senderAddress), StringComparison.Ordinal);

internal sealed record SendEmailRequest(
    string To,
    string Subject,
    string? Text = null,
    string? Html = null);
