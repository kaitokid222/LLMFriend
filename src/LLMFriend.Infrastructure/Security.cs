using System.Security.Cryptography;
using System.Text;
using Dapper;
using LLMFriend.Core;
using Microsoft.AspNetCore.Identity;

namespace LLMFriend.Infrastructure;

public sealed class TokenCipher
{
    private readonly byte[] _key;

    public TokenCipher(string base64Key)
    {
        try { _key = Convert.FromBase64String(base64Key); }
        catch (FormatException ex) { throw new ArgumentException("LLMFRIEND_ENCRYPTION_KEY muss Base64 sein.", ex); }
        if (_key.Length != 32) throw new ArgumentException("LLMFRIEND_ENCRYPTION_KEY muss 32 Bytes enthalten.");
    }

    public string Encrypt(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value);
        var output = new byte[12 + 16 + plain.Length];
        nonce.CopyTo(output, 0);
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, output.AsSpan(28), output.AsSpan(12, 16));
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string value)
    {
        var input = Convert.FromBase64String(value);
        if (input.Length < 28) throw new CryptographicException("Ungültiger Ciphertext.");
        var plain = new byte[input.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(input.AsSpan(0, 12), input.AsSpan(28), input.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
}

public sealed class UserService(Database database)
{
    private readonly PasswordHasher<UserRow> _hasher = new();

    public async Task<bool> HasAdminAsync()
    {
        await using var connection = database.Open();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_users WHERE is_admin=TRUE") > 0;
    }

    public async Task<UserRow?> FindAsync(string username)
    {
        await using var connection = database.Open();
        return await connection.QuerySingleOrDefaultAsync<UserRow>("SELECT * FROM app_users WHERE username=@username", new { username });
    }

    public async Task<UserRow?> FindByIdAsync(string id)
    {
        await using var connection = database.Open();
        return await connection.QuerySingleOrDefaultAsync<UserRow>("SELECT * FROM app_users WHERE id=@id", new { id });
    }

    public async Task<IReadOnlyList<UserRow>> ListAsync()
    {
        await using var connection = database.Open();
        return (await connection.QueryAsync<UserRow>("SELECT id, username, is_admin, must_change_password, created_at FROM app_users ORDER BY username")).AsList();
    }

    public async Task<(UserRow User, string Password)> CreateAsync(string username, bool isAdmin)
    {
        username = username.Trim();
        if (username.Length is < 3 or > 100 || username.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ArgumentException("Nutzername: 3–100 Buchstaben, Ziffern, Punkt, Bindestrich oder Unterstrich.");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var user = new UserRow { Id = Guid.NewGuid().ToString(), Username = username, IsAdmin = isAdmin, MustChangePassword = true, CreatedAt = DateTime.UtcNow };
        user.PasswordHash = _hasher.HashPassword(user, password);
        await using var connection = database.Open();
        await connection.ExecuteAsync("INSERT INTO app_users(id,username,password_hash,is_admin,must_change_password,created_at) VALUES(@Id,@Username,@PasswordHash,@IsAdmin,@MustChangePassword,@CreatedAt)", user);
        return (user, password);
    }

    public async Task<UserRow?> VerifyAsync(string username, string password)
    {
        var user = await FindAsync(username);
        if (user is null) return null;
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }

    public async Task ChangePasswordAsync(UserRow user, string newPassword)
    {
        if (newPassword.Length < 12 || newPassword.Length > 256) throw new ArgumentException("Passwort muss 12–256 Zeichen haben.");
        var hash = _hasher.HashPassword(user, newPassword);
        await using var connection = database.Open();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("UPDATE app_users SET password_hash=@hash, must_change_password=FALSE WHERE id=@id", new { hash, id = user.Id }, transaction);
        await connection.ExecuteAsync("UPDATE sessions SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND revoked_at IS NULL", new { id = user.Id }, transaction);
        await transaction.CommitAsync();
    }

    public async Task<string> ResetPasswordAsync(string id)
    {
        var user = await FindByIdAsync(id) ?? throw new KeyNotFoundException("Nutzer nicht gefunden.");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var hash = _hasher.HashPassword(user, password);
        await using var connection = database.Open();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("UPDATE app_users SET password_hash=@hash, must_change_password=TRUE WHERE id=@id", new { hash, id }, transaction);
        await connection.ExecuteAsync("UPDATE sessions SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND revoked_at IS NULL", new { id }, transaction);
        await transaction.CommitAsync();
        return password;
    }
}

public sealed class SessionService(Database database)
{
    public async Task<string> CreateAsync(string userId)
    {
        var token = OAuthSecurity.NewRandomValue();
        await using var connection = database.Open();
        await connection.ExecuteAsync("INSERT INTO sessions(id,user_id,token_hash,expires_at) VALUES(@id,@userId,@hash,@expiresAt)", new { id = Guid.NewGuid().ToString(), userId, hash = OAuthSecurity.Hash(token), expiresAt = DateTime.UtcNow.AddDays(7) });
        return token;
    }

    public async Task<UserRow?> AuthenticateAsync(string token)
    {
        await using var connection = database.Open();
        return await connection.QuerySingleOrDefaultAsync<UserRow>("SELECT u.* FROM sessions s JOIN app_users u ON u.id=s.user_id WHERE s.token_hash=@hash AND s.revoked_at IS NULL AND s.expires_at>UTC_TIMESTAMP(6)", new { hash = OAuthSecurity.Hash(token) });
    }

    public async Task RevokeAsync(string token)
    {
        await using var connection = database.Open();
        await connection.ExecuteAsync("UPDATE sessions SET revoked_at=UTC_TIMESTAMP(6) WHERE token_hash=@hash", new { hash = OAuthSecurity.Hash(token) });
    }
}
