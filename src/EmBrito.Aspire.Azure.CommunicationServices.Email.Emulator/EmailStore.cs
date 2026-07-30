using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal sealed class EmailStore(IConfiguration configuration)
{
    private readonly string _connectionString = CreateConnectionString(configuration);

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        var databaseDirectory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Messages (
                OperationId TEXT NOT NULL PRIMARY KEY,
                ApiVersion TEXT NOT NULL,
                ClientRequestId TEXT NULL,
                CapturedAtUtc TEXT NOT NULL,
                SenderAddress TEXT NOT NULL,
                Subject TEXT NOT NULL,
                PlainText TEXT NULL,
                Html TEXT NULL,
                UserEngagementTrackingDisabled INTEGER NULL,
                RawJson TEXT NOT NULL,
                SearchText TEXT NOT NULL,
                Deleted INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Recipients (
                OperationId TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                Ordinal INTEGER NOT NULL,
                Address TEXT NOT NULL,
                DisplayName TEXT NULL,
                PRIMARY KEY (OperationId, Kind, Ordinal),
                FOREIGN KEY (OperationId) REFERENCES Messages(OperationId) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Headers (
                OperationId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Value TEXT NOT NULL,
                PRIMARY KEY (OperationId, Name),
                FOREIGN KEY (OperationId) REFERENCES Messages(OperationId) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Attachments (
                OperationId TEXT NOT NULL,
                AttachmentIndex INTEGER NOT NULL,
                Name TEXT NOT NULL,
                ContentType TEXT NOT NULL,
                ContentId TEXT NULL,
                Content BLOB NOT NULL,
                PRIMARY KEY (OperationId, AttachmentIndex),
                FOREIGN KEY (OperationId) REFERENCES Messages(OperationId) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Messages_CapturedAtUtc
                ON Messages(CapturedAtUtc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<CaptureResult> CaptureAsync(
        ParsedEmail email,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var existingCommand = connection.CreateCommand())
        {
            existingCommand.Transaction = transaction;
            existingCommand.CommandText =
                "SELECT RawJson FROM Messages WHERE OperationId = $operationId;";
            existingCommand.Parameters.AddWithValue("$operationId", email.OperationId.ToString("D"));
            var existing = await existingCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (existing is string existingRawJson)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return string.Equals(existingRawJson, email.RawJson, StringComparison.Ordinal)
                    ? CaptureResult.Duplicate
                    : CaptureResult.Conflict;
            }
        }

        await using (var messageCommand = connection.CreateCommand())
        {
            messageCommand.Transaction = transaction;
            messageCommand.CommandText =
                """
                INSERT INTO Messages (
                    OperationId,
                    ApiVersion,
                    ClientRequestId,
                    CapturedAtUtc,
                    SenderAddress,
                    Subject,
                    PlainText,
                    Html,
                    UserEngagementTrackingDisabled,
                    RawJson,
                    SearchText)
                VALUES (
                    $operationId,
                    $apiVersion,
                    $clientRequestId,
                    $capturedAtUtc,
                    $senderAddress,
                    $subject,
                    $plainText,
                    $html,
                    $trackingDisabled,
                    $rawJson,
                    $searchText);
                """;
            messageCommand.Parameters.AddWithValue("$operationId", email.OperationId.ToString("D"));
            messageCommand.Parameters.AddWithValue("$apiVersion", email.ApiVersion);
            messageCommand.Parameters.AddWithValue(
                "$clientRequestId",
                (object?)email.ClientRequestId ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue(
                "$capturedAtUtc",
                email.CapturedAt.ToString("O", CultureInfo.InvariantCulture));
            messageCommand.Parameters.AddWithValue("$senderAddress", email.SenderAddress);
            messageCommand.Parameters.AddWithValue("$subject", email.Subject);
            messageCommand.Parameters.AddWithValue("$plainText", (object?)email.PlainText ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue("$html", (object?)email.Html ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue(
                "$trackingDisabled",
                email.UserEngagementTrackingDisabled is null
                    ? DBNull.Value
                    : email.UserEngagementTrackingDisabled.Value ? 1 : 0);
            messageCommand.Parameters.AddWithValue("$rawJson", email.RawJson);
            messageCommand.Parameters.AddWithValue("$searchText", email.SearchText);
            await messageCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var recipient in email.Recipients)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Recipients (OperationId, Kind, Ordinal, Address, DisplayName)
                VALUES ($operationId, $kind, $ordinal, $address, $displayName);
                """;
            command.Parameters.AddWithValue("$operationId", email.OperationId.ToString("D"));
            command.Parameters.AddWithValue("$kind", (int)recipient.Kind);
            command.Parameters.AddWithValue("$ordinal", recipient.Ordinal);
            command.Parameters.AddWithValue("$address", recipient.Address);
            command.Parameters.AddWithValue(
                "$displayName",
                (object?)recipient.DisplayName ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var header in email.Headers)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Headers (OperationId, Name, Value)
                VALUES ($operationId, $name, $value);
                """;
            command.Parameters.AddWithValue("$operationId", email.OperationId.ToString("D"));
            command.Parameters.AddWithValue("$name", header.Key);
            command.Parameters.AddWithValue("$value", header.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var attachment in email.Attachments)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Attachments (
                    OperationId,
                    AttachmentIndex,
                    Name,
                    ContentType,
                    ContentId,
                    Content)
                VALUES (
                    $operationId,
                    $attachmentIndex,
                    $name,
                    $contentType,
                    $contentId,
                    $content);
                """;
            command.Parameters.AddWithValue("$operationId", email.OperationId.ToString("D"));
            command.Parameters.AddWithValue("$attachmentIndex", attachment.Index);
            command.Parameters.AddWithValue("$name", attachment.Name);
            command.Parameters.AddWithValue("$contentType", attachment.ContentType);
            command.Parameters.AddWithValue("$contentId", (object?)attachment.ContentId ?? DBNull.Value);
            command.Parameters.AddWithValue("$content", attachment.Content);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return CaptureResult.Created;
    }

    internal async Task<bool> OperationExistsAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM Messages WHERE OperationId = $operationId;";
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    internal async Task<IReadOnlyList<EmailSummary>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                m.OperationId,
                m.CapturedAtUtc,
                m.SenderAddress,
                m.Subject,
                COALESCE(GROUP_CONCAT(r.Address, ', '), '')
            FROM Messages m
            LEFT JOIN Recipients r
                ON r.OperationId = m.OperationId AND r.Kind IN (0, 1, 2)
            WHERE
                m.Deleted = 0
                AND ($query = '' OR m.SearchText LIKE $pattern)
            GROUP BY
                m.OperationId,
                m.CapturedAtUtc,
                m.SenderAddress,
                m.Subject
            ORDER BY m.CapturedAtUtc DESC
            LIMIT 200;
            """;
        var normalizedQuery = query?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$pattern", $"%{normalizedQuery}%");

        var result = new List<EmailSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new EmailSummary(
                    Guid.Parse(reader.GetString(0)),
                    DateTimeOffset.Parse(
                        reader.GetString(1),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
        }

        return result;
    }

    internal async Task<StoredEmail?> GetAsync(
        Guid operationId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var messageCommand = connection.CreateCommand();
        messageCommand.CommandText =
            """
            SELECT
                ApiVersion,
                ClientRequestId,
                CapturedAtUtc,
                SenderAddress,
                Subject,
                PlainText,
                Html,
                UserEngagementTrackingDisabled,
                RawJson
            FROM Messages
            WHERE OperationId = $operationId AND ($includeDeleted = 1 OR Deleted = 0);
            """;
        messageCommand.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        messageCommand.Parameters.AddWithValue("$includeDeleted", includeDeleted ? 1 : 0);

        string apiVersion;
        string? clientRequestId;
        DateTimeOffset capturedAt;
        string senderAddress;
        string subject;
        string? plainText;
        string? html;
        bool? trackingDisabled;
        string rawJson;

        await using (var reader = await messageCommand
                         .ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            apiVersion = reader.GetString(0);
            clientRequestId = reader.IsDBNull(1) ? null : reader.GetString(1);
            capturedAt = DateTimeOffset.Parse(
                reader.GetString(2),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            senderAddress = reader.GetString(3);
            subject = reader.GetString(4);
            plainText = reader.IsDBNull(5) ? null : reader.GetString(5);
            html = reader.IsDBNull(6) ? null : reader.GetString(6);
            trackingDisabled = reader.IsDBNull(7) ? null : reader.GetInt32(7) != 0;
            rawJson = reader.GetString(8);
        }

        var recipients = await ReadRecipientsAsync(connection, operationId, cancellationToken)
            .ConfigureAwait(false);
        var headers = await ReadHeadersAsync(connection, operationId, cancellationToken)
            .ConfigureAwait(false);
        var attachments = await ReadAttachmentsAsync(connection, operationId, cancellationToken)
            .ConfigureAwait(false);

        return new StoredEmail(
            operationId,
            apiVersion,
            clientRequestId,
            capturedAt,
            senderAddress,
            subject,
            plainText,
            html,
            trackingDisabled,
            rawJson,
            recipients,
            headers,
            attachments);
    }

    internal async Task<AttachmentContent?> GetAttachmentAsync(
        Guid operationId,
        int attachmentIndex,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.Name, a.ContentType, a.Content
            FROM Attachments a
            JOIN Messages m ON m.OperationId = a.OperationId
            WHERE
                a.OperationId = $operationId
                AND a.AttachmentIndex = $attachmentIndex
                AND m.Deleted = 0;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        command.Parameters.AddWithValue("$attachmentIndex", attachmentIndex);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new AttachmentContent(
            reader.GetString(0),
            reader.GetString(1),
            (byte[])reader[2]);
    }

    internal async Task<bool> DeleteAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Messages SET Deleted = 1 WHERE OperationId = $operationId AND Deleted = 0;";
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    internal async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Messages SET Deleted = 1 WHERE Deleted = 0;";
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ParsedRecipient>> ReadRecipientsAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Kind, Ordinal, Address, DisplayName
            FROM Recipients
            WHERE OperationId = $operationId
            ORDER BY Kind, Ordinal;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));

        var result = new List<ParsedRecipient>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new ParsedRecipient(
                    (RecipientKind)reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadHeadersAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Name, Value
            FROM Headers
            WHERE OperationId = $operationId
            ORDER BY Name;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0), reader.GetString(1));
        }

        return result;
    }

    private static async Task<IReadOnlyList<StoredAttachment>> ReadAttachmentsAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT AttachmentIndex, Name, ContentType, ContentId, LENGTH(Content)
            FROM Attachments
            WHERE OperationId = $operationId
            ORDER BY AttachmentIndex;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));

        var result = new List<StoredAttachment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new StoredAttachment(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt64(4)));
        }

        return result;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string CreateConnectionString(IConfiguration configuration)
    {
        var configuredPath = configuration["Emulator:DatabasePath"];
        var databasePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "data", "email.db")
            : configuredPath;

        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }
}
