using System.Reflection;
using Dapper;
using MySqlConnector;

namespace LLMFriend.Infrastructure;

public sealed class Database
{
    private readonly string _connectionString;

    public Database(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("LLMFRIEND_DB fehlt.");
        // IDs are stored and mapped as strings throughout the application.
        // MySqlConnector otherwise converts every CHAR(36) column to Guid.
        _connectionString = new MySqlConnectionStringBuilder(connectionString)
        {
            GuidFormat = MySqlGuidFormat.None
        }.ConnectionString;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public MySqlConnection Open() => new(_connectionString);

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken);
        var assembly = typeof(Database).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith("001_initial.sql", StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT IGNORE INTO schema_migrations(version, applied_at) VALUES(1, UTC_TIMESTAMP(6))", cancellationToken: cancellationToken));
    }
}

public sealed class UserRow
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AccountRow
{
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerUsername { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? EncryptedAccessToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public string? SnapshotJson { get; set; }
    public string Status { get; set; } = "";
    public string? LastError { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? NextCheckAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsEditor { get; set; }
}

public sealed class OAuthPendingRow
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string StateHash { get; set; } = "";
    public string EncryptedVerifier { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? AccountId { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class AccountEventRow
{
    public long Id { get; set; }
    public string AccountId { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
}
