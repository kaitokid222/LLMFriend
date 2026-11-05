using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LLMFriend.Core;

namespace LLMFriend.Infrastructure;

public sealed class DiscordOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";

    public static DiscordOptions FromEnvironment() => new()
    {
        ClientId = Environment.GetEnvironmentVariable("LLMFRIEND_DISCORD_CLIENT_ID") ?? "",
        ClientSecret = Environment.GetEnvironmentVariable("LLMFRIEND_DISCORD_CLIENT_SECRET") ?? "",
        RedirectUri = Environment.GetEnvironmentVariable("LLMFRIEND_DISCORD_REDIRECT_URI") ?? ""
    };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret) ||
            !Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Host != "localhost" && uri.Host != "127.0.0.1"))
            throw new InvalidOperationException("Discord Client ID, Secret und HTTPS-Redirect-URI konfigurieren.");
    }
}

public sealed class ConnectorRateLimitException(TimeSpan retryAfter) : Exception("Discord-Rate-Limit erreicht.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public sealed class DiscordConnector(HttpClient http, DiscordOptions options) : IAccountConnector
{
    public string Provider => "discord";

    public string CreateAuthorizationUrl(string state, string codeChallenge)
    {
        options.Validate();
        var values = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri, ["scope"] = "identify guilds",
            ["state"] = state, ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256", ["prompt"] = "consent"
        };
        return "https://discord.com/oauth2/authorize?" + string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public Task<OAuthTokens> ExchangeCodeAsync(string code, string verifier, CancellationToken ct) =>
        RequestTokensAsync(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = options.RedirectUri, ["code_verifier"] = verifier }, ct);

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        RequestTokensAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    private async Task<OAuthTokens> RequestTokensAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        options.Validate();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}")));
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        return new OAuthTokens(root.GetProperty("access_token").GetString()!, root.GetProperty("refresh_token").GetString()!,
            DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    public async Task<ConnectorSnapshot> ReadSnapshotAsync(string accessToken, CancellationToken ct)
    {
        using var profile = await GetAsync("users/@me", accessToken, ct);
        using var guilds = await GetAsync("users/@me/guilds", accessToken, ct);
        var p = profile.RootElement;
        var id = p.GetProperty("id").GetString()!;
        var username = p.GetProperty("username").GetString()!;
        var displayName = p.TryGetProperty("global_name", out var global) && global.ValueKind == JsonValueKind.String ? global.GetString()! : username;
        string? avatarUrl = null;
        if (p.TryGetProperty("avatar", out var avatar) && avatar.ValueKind == JsonValueKind.String)
            avatarUrl = $"https://cdn.discordapp.com/avatars/{id}/{avatar.GetString()}.png";
        var items = new List<ExternalGuild>();
        foreach (var g in guilds.RootElement.EnumerateArray())
        {
            var guildId = g.GetProperty("id").GetString()!;
            var icon = g.TryGetProperty("icon", out var iconElement) && iconElement.ValueKind == JsonValueKind.String
                ? $"https://cdn.discordapp.com/icons/{guildId}/{iconElement.GetString()}.png" : null;
            items.Add(new ExternalGuild(guildId, g.GetProperty("name").GetString()!, icon));
        }
        return new ConnectorSnapshot(new ExternalProfile(id, username, displayName, avatarUrl), items);
    }

    private async Task<JsonDocument> GetAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        options.Validate();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/v10/oauth2/token/revoke")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}")));
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
            throw new ConnectorRateLimitException(retry);
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Discord API: {(int)response.StatusCode}", null, response.StatusCode);
        return Task.CompletedTask;
    }
}
