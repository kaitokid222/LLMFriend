using System.Net;
using System.Text.Json;
using Dapper;
using LLMFriend.Core;

namespace LLMFriend.Infrastructure;

public sealed class AccountService(Database database, TokenCipher cipher, IAccountConnector connector)
{
    public async Task<IReadOnlyList<AccountRow>> ListVisibleAsync(string actorId)
    {
        await using var db = database.Open();
        return (await db.QueryAsync<AccountRow>("""
            SELECT a.*, u.username AS owner_username, (s.user_id IS NOT NULL) AS is_editor
            FROM accounts a JOIN app_users u ON u.id=a.owner_id
            LEFT JOIN account_shares s ON s.account_id=a.id AND s.user_id=@actorId
            WHERE a.owner_id=@actorId OR s.user_id IS NOT NULL ORDER BY a.provider,a.display_name
            """, new { actorId })).AsList();
    }

    public async Task<AccountRow?> GetVisibleAsync(string id, string actorId)
    {
        await using var db = database.Open();
        return await db.QuerySingleOrDefaultAsync<AccountRow>("""
            SELECT a.*, u.username AS owner_username, (s.user_id IS NOT NULL) AS is_editor
            FROM accounts a JOIN app_users u ON u.id=a.owner_id
            LEFT JOIN account_shares s ON s.account_id=a.id AND s.user_id=@actorId
            WHERE a.id=@id AND (a.owner_id=@actorId OR s.user_id IS NOT NULL)
            """, new { id, actorId });
    }

    public async Task<AccountRow?> GetForWorkerAsync(string id)
    {
        await using var db = database.Open();
        return await db.QuerySingleOrDefaultAsync<AccountRow>("SELECT a.*, u.username AS owner_username FROM accounts a JOIN app_users u ON u.id=a.owner_id WHERE a.id=@id", new { id });
    }

    public async Task<IReadOnlyList<AccountRow>> DueAsync()
    {
        await using var db = database.Open();
        return (await db.QueryAsync<AccountRow>("""
            SELECT * FROM accounts WHERE status IN ('connected','error') AND next_check_at<=UTC_TIMESTAMP(6)
            ORDER BY next_check_at LIMIT 50
            """)).AsList();
    }

    public async Task<(string PendingId, string Url)> BeginLinkAsync(string userId)
    {
        var state = OAuthSecurity.NewRandomValue();
        var verifier = OAuthSecurity.NewRandomValue();
        var id = Guid.NewGuid().ToString();
        var url = connector.CreateAuthorizationUrl(state, OAuthSecurity.Challenge(verifier));
        await using var db = database.Open();
        await db.ExecuteAsync("""
            INSERT INTO oauth_pending(id,user_id,state_hash,encrypted_verifier,expires_at)
            VALUES(@id,@userId,@stateHash,@encryptedVerifier,@expiresAt)
            """, new { id, userId, stateHash = OAuthSecurity.Hash(state), encryptedVerifier = cipher.Encrypt(verifier), expiresAt = DateTime.UtcNow.AddMinutes(10) });
        return (id, url);
    }

    public async Task<OAuthPendingRow?> GetPendingAsync(string pendingId, string actorId)
    {
        await using var db = database.Open();
        return await db.QuerySingleOrDefaultAsync<OAuthPendingRow>("SELECT * FROM oauth_pending WHERE id=@pendingId AND user_id=@actorId", new { pendingId, actorId });
    }

    public async Task FailLinkAsync(string state, string message)
    {
        await using var db = database.Open();
        await db.ExecuteAsync("UPDATE oauth_pending SET completed_at=UTC_TIMESTAMP(6),error_message=@message WHERE state_hash=@stateHash AND completed_at IS NULL AND expires_at>UTC_TIMESTAMP(6)",
            new { stateHash = OAuthSecurity.Hash(state), message = message[..Math.Min(message.Length, 500)] });
    }

    public async Task CompleteLinkAsync(string state, string code, CancellationToken ct)
    {
        var stateHash = OAuthSecurity.Hash(state);
        await using var db = database.Open();
        var pending = await db.QuerySingleOrDefaultAsync<OAuthPendingRow>("SELECT * FROM oauth_pending WHERE state_hash=@stateHash AND completed_at IS NULL AND expires_at>UTC_TIMESTAMP(6)", new { stateHash })
            ?? throw new InvalidOperationException("OAuth-Vorgang ungültig oder abgelaufen.");
        var claimed = await db.ExecuteAsync("UPDATE oauth_pending SET completed_at=UTC_TIMESTAMP(6) WHERE id=@id AND completed_at IS NULL", new { pending.Id });
        if (claimed != 1) throw new InvalidOperationException("OAuth-Vorgang wurde bereits verwendet.");

        try
        {
            var tokens = await connector.ExchangeCodeAsync(code, cipher.Decrypt(pending.EncryptedVerifier), ct);
            var snapshot = await connector.ReadSnapshotAsync(tokens.AccessToken, ct);
            var accountId = await SaveLinkedAsync(pending.UserId, tokens, snapshot);
            await db.ExecuteAsync("UPDATE oauth_pending SET account_id=@accountId WHERE id=@id", new { accountId, pending.Id });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await db.ExecuteAsync("UPDATE oauth_pending SET error_message=@message WHERE id=@id", new { message = SafeError(ex), pending.Id });
            throw;
        }
    }

    private async Task<string> SaveLinkedAsync(string ownerId, OAuthTokens tokens, ConnectorSnapshot snapshot)
    {
        await using var db = database.Open();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        var existing = await db.QuerySingleOrDefaultAsync<AccountRow>("SELECT * FROM accounts WHERE owner_id=@ownerId AND provider=@provider AND external_id=@externalId", new { ownerId, provider = connector.Provider, externalId = snapshot.Profile.Id }, tx);
        var id = existing?.Id ?? Guid.NewGuid().ToString();
        if (existing is null)
        {
            await db.ExecuteAsync("""
                INSERT INTO accounts(id,owner_id,provider,external_id,username,display_name,avatar_url,encrypted_access_token,encrypted_refresh_token,token_expires_at,snapshot_json,status,last_checked_at,next_check_at,created_at)
                VALUES(@id,@ownerId,@provider,@externalId,@username,@displayName,@avatarUrl,@access,@refresh,@expires,@snapshot,'connected',@now,@next,@now)
                """, new { id, ownerId, provider = connector.Provider, externalId = snapshot.Profile.Id, username = snapshot.Profile.Username, displayName = snapshot.Profile.DisplayName, avatarUrl = snapshot.Profile.AvatarUrl,
                    access = cipher.Encrypt(tokens.AccessToken), refresh = cipher.Encrypt(tokens.RefreshToken), expires = tokens.ExpiresAtUtc, snapshot = JsonSerializer.Serialize(snapshot), now = DateTime.UtcNow, next = DateTime.UtcNow.AddMinutes(15) }, tx);
        }
        else
        {
            await db.ExecuteAsync("""
                UPDATE accounts SET username=@username,display_name=@displayName,avatar_url=@avatarUrl,encrypted_access_token=@access,
                encrypted_refresh_token=@refresh,token_expires_at=@expires,snapshot_json=@snapshot,status='connected',last_error=NULL,last_checked_at=@now,next_check_at=@next WHERE id=@id
                """, new { id, username = snapshot.Profile.Username, displayName = snapshot.Profile.DisplayName, avatarUrl = snapshot.Profile.AvatarUrl,
                    access = cipher.Encrypt(tokens.AccessToken), refresh = cipher.Encrypt(tokens.RefreshToken), expires = tokens.ExpiresAtUtc, snapshot = JsonSerializer.Serialize(snapshot), now = DateTime.UtcNow, next = DateTime.UtcNow.AddMinutes(15) }, tx);
        }
        await AddEventAsync(db, tx, id, existing is null ? "connected" : "reconnected", existing is null ? "Discord-Konto verbunden" : "Discord-Konto erneut autorisiert");
        await tx.CommitAsync();
        return id;
    }

    public async Task SaveMonitoringAsync(AccountRow account, ConnectorSnapshot snapshot, OAuthTokens? rotatedTokens)
    {
        ConnectorSnapshot? previous = account.SnapshotJson is null ? null : JsonSerializer.Deserialize<ConnectorSnapshot>(account.SnapshotJson);
        var changes = SnapshotChanges.Compare(previous, snapshot);
        await using var db = database.Open();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        var updated = await db.ExecuteAsync("""
            UPDATE accounts SET username=@username,display_name=@displayName,avatar_url=@avatarUrl,snapshot_json=@snapshot,status='connected',
            last_error=NULL,last_checked_at=@now,next_check_at=@next,
            encrypted_access_token=COALESCE(@access,encrypted_access_token),encrypted_refresh_token=COALESCE(@refresh,encrypted_refresh_token),
            token_expires_at=COALESCE(@expires,token_expires_at) WHERE id=@id AND status IN ('connected','error')
            """, new { account.Id, username = snapshot.Profile.Username, displayName = snapshot.Profile.DisplayName, avatarUrl = snapshot.Profile.AvatarUrl,
                snapshot = JsonSerializer.Serialize(snapshot), now = DateTime.UtcNow, next = DateTime.UtcNow.AddMinutes(15),
                access = rotatedTokens is null ? null : cipher.Encrypt(rotatedTokens.AccessToken), refresh = rotatedTokens is null ? null : cipher.Encrypt(rotatedTokens.RefreshToken), expires = rotatedTokens?.ExpiresAtUtc }, tx);
        if (updated == 0) { await tx.RollbackAsync(); return; }
        foreach (var change in changes) await AddEventAsync(db, tx, account.Id, change.Kind, change.Description);
        if (account.Status == "error") await AddEventAsync(db, tx, account.Id, "recovered", "Verbindung wiederhergestellt");
        await tx.CommitAsync();
    }

    public async Task SaveMonitoringErrorAsync(AccountRow account, Exception ex)
    {
        var retry = ex is ConnectorRateLimitException rate ? rate.RetryAfter : TimeSpan.FromMinutes(15);
        if (retry < TimeSpan.FromSeconds(30)) retry = TimeSpan.FromSeconds(30);
        var message = SafeError(ex);
        await using var db = database.Open();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        var updated = await db.ExecuteAsync("UPDATE accounts SET status='error',last_error=@message,last_checked_at=@now,next_check_at=@next WHERE id=@id AND status IN ('connected','error')",
            new { account.Id, message, now = DateTime.UtcNow, next = DateTime.UtcNow.Add(retry) }, tx);
        if (updated == 0) { await tx.RollbackAsync(); return; }
        if (account.Status != "error" || account.LastError != message) await AddEventAsync(db, tx, account.Id, "error", message);
        await tx.CommitAsync();
    }

    public async Task RequestRefreshAsync(string id)
    {
        await using var db = database.Open();
        await db.ExecuteAsync("UPDATE accounts SET next_check_at=UTC_TIMESTAMP(6) WHERE id=@id AND status IN ('connected','error')", new { id });
    }

    public async Task SaveRotatedTokensAsync(string id, OAuthTokens tokens)
    {
        await using var db = database.Open();
        await db.ExecuteAsync("UPDATE accounts SET encrypted_access_token=@access,encrypted_refresh_token=@refresh,token_expires_at=@expires WHERE id=@id AND status IN ('connected','error')",
            new { id, access = cipher.Encrypt(tokens.AccessToken), refresh = cipher.Encrypt(tokens.RefreshToken), expires = tokens.ExpiresAtUtc });
    }

    public async Task<IReadOnlyList<AccountEventRow>> EventsAsync(string id, long? before)
    {
        await using var db = database.Open();
        return (await db.QueryAsync<AccountEventRow>("SELECT * FROM account_events WHERE account_id=@id AND (@before IS NULL OR id<@before) ORDER BY id DESC LIMIT 100", new { id, before })).AsList();
    }

    public async Task<IReadOnlyList<UserRow>> SharesAsync(string id)
    {
        await using var db = database.Open();
        var rows = await db.QueryAsync<UserRow>("SELECT u.id,u.username FROM account_shares s JOIN app_users u ON u.id=s.user_id WHERE s.account_id=@id ORDER BY u.username", new { id });
        return rows.AsList();
    }

    public async Task ShareAsync(string accountId, string userId)
    {
        await using var db = database.Open();
        var exists = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM accounts a JOIN app_users u ON u.id=@userId WHERE a.id=@accountId AND a.owner_id<>@userId", new { accountId, userId });
        if (exists == 0) throw new ArgumentException("Nutzer nicht gefunden oder Kontoinhaber.");
        await db.ExecuteAsync("INSERT IGNORE INTO account_shares(account_id,user_id,created_at) VALUES(@accountId,@userId,UTC_TIMESTAMP(6))", new { accountId, userId });
    }

    public async Task UnshareAsync(string accountId, string userId)
    {
        await using var db = database.Open();
        await db.ExecuteAsync("DELETE FROM account_shares WHERE account_id=@accountId AND user_id=@userId", new { accountId, userId });
    }

    public async Task DisconnectAsync(AccountRow account, CancellationToken ct)
    {
        if (account.EncryptedRefreshToken is not null)
        {
            try { await connector.RevokeAsync(cipher.Decrypt(account.EncryptedRefreshToken), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* Local token removal still takes precedence. */ }
        }
        await using var db = database.Open();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        await db.ExecuteAsync("UPDATE accounts SET encrypted_access_token=NULL,encrypted_refresh_token=NULL,token_expires_at=NULL,status='disconnected',next_check_at=NULL,last_error=NULL WHERE id=@id", new { account.Id }, tx);
        await AddEventAsync(db, tx, account.Id, "disconnected", "Discord-Konto getrennt");
        await tx.CommitAsync();
    }

    public async Task DeleteAsync(string id)
    {
        await using var db = database.Open();
        await db.ExecuteAsync("DELETE FROM accounts WHERE id=@id", new { id });
    }

    private static async Task AddEventAsync(MySqlConnector.MySqlConnection db, System.Data.Common.DbTransaction tx, string accountId, string kind, string description) =>
        await db.ExecuteAsync("INSERT INTO account_events(account_id,occurred_at,kind,description) VALUES(@accountId,UTC_TIMESTAMP(6),@kind,@description)", new { accountId, kind, description }, tx);

    private static string SafeError(Exception ex) => ex switch
    {
        ConnectorRateLimitException => "Discord-Rate-Limit erreicht",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "Discord-Autorisierung ungültig",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "Discord-Zugriff verweigert",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "Discord-Rate-Limit erreicht",
        HttpRequestException => "Discord-Anfrage fehlgeschlagen",
        _ => "Verbindungsprüfung fehlgeschlagen"
    };
}

public sealed class MonitorService(AccountService accounts, TokenCipher cipher, IAccountConnector connector)
{
    public async Task CheckAsync(string id, CancellationToken ct)
    {
        var account = await accounts.GetForWorkerAsync(id) ?? throw new KeyNotFoundException("Konto nicht gefunden.");
        if (account.Status is not ("connected" or "error") || account.EncryptedAccessToken is null || account.EncryptedRefreshToken is null) return;
        try
        {
            OAuthTokens? rotated = null;
            var access = cipher.Decrypt(account.EncryptedAccessToken);
            if (account.TokenExpiresAt is null || account.TokenExpiresAt <= DateTime.UtcNow.AddMinutes(2))
            {
                rotated = await connector.RefreshAsync(cipher.Decrypt(account.EncryptedRefreshToken), ct);
                await accounts.SaveRotatedTokensAsync(account.Id, rotated);
                access = rotated.AccessToken;
            }
            var snapshot = await connector.ReadSnapshotAsync(access, ct);
            await accounts.SaveMonitoringAsync(account, snapshot, rotated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { await accounts.SaveMonitoringErrorAsync(account, ex); }
    }
}
