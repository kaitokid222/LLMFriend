using System.Security.Cryptography;
using System.Text;

namespace LLMFriend.Core;

public sealed record ExternalProfile(string Id, string Username, string DisplayName, string? AvatarUrl);
public sealed record ExternalGuild(string Id, string Name, string? IconUrl);
public sealed record ConnectorSnapshot(ExternalProfile Profile, IReadOnlyList<ExternalGuild> Guilds);
public sealed record OAuthTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);
public sealed record AccountChange(string Kind, string Description);

public interface IAccountConnector
{
    string Provider { get; }
    string CreateAuthorizationUrl(string state, string codeChallenge);
    Task<OAuthTokens> ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken);
    Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<ConnectorSnapshot> ReadSnapshotAsync(string accessToken, CancellationToken cancellationToken);
    Task RevokeAsync(string token, CancellationToken cancellationToken);
}

public static class AccountAccess
{
    public static bool CanManage(string actorId, string ownerId, bool isEditor) => actorId == ownerId || isEditor;
    public static bool CanDelete(string actorId, string ownerId) => actorId == ownerId;
    public static bool CanReauthorize(string actorId, string ownerId) => actorId == ownerId;
}

public static class SnapshotChanges
{
    public static IReadOnlyList<AccountChange> Compare(ConnectorSnapshot? previous, ConnectorSnapshot current)
    {
        if (previous is null)
            return [new("connected", "Discord-Konto verbunden")];

        var changes = new List<AccountChange>();
        if (previous.Profile.Username != current.Profile.Username || previous.Profile.DisplayName != current.Profile.DisplayName)
            changes.Add(new("profile", "Profilname geändert"));
        if (previous.Profile.AvatarUrl != current.Profile.AvatarUrl)
            changes.Add(new("profile", "Profilbild geändert"));

        var oldGuilds = previous.Guilds.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var newGuilds = current.Guilds.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var guild in current.Guilds.Where(x => !oldGuilds.ContainsKey(x.Id)))
            changes.Add(new("guild_joined", $"Server hinzugefügt: {guild.Name}"));
        foreach (var guild in previous.Guilds.Where(x => !newGuilds.ContainsKey(x.Id)))
            changes.Add(new("guild_left", $"Server entfernt: {guild.Name}"));
        foreach (var guild in current.Guilds.Where(x => oldGuilds.TryGetValue(x.Id, out var old) && old.Name != x.Name))
            changes.Add(new("guild", $"Server umbenannt: {oldGuilds[guild.Id].Name} → {guild.Name}"));
        return changes;
    }
}

public static class OAuthSecurity
{
    public static string NewRandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
