using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LLMFriend.Wpf;

public sealed class ApiClient : IDisposable
{
    private HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(string url)
    {
        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Host != "localhost" && uri.Host != "127.0.0.1"))
            throw new ArgumentException("HTTPS-API-Adresse oder lokales HTTP angeben.");
        if (_http.BaseAddress != uri)
        {
            _http.Dispose();
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30), BaseAddress = uri };
        }
    }

    public void SetToken(string token) => _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    public void ClearToken() => _http.DefaultRequestHeaders.Authorization = null;

    public Task<LoginResponse> LoginAsync(string username, string password) => PostAsync<LoginResponse>("api/auth/login", new { username, password });
    public Task<List<AccountDto>> AccountsAsync() => GetAsync<List<AccountDto>>("api/accounts");
    public Task<AccountDetailDto> DetailAsync(string id) => GetAsync<AccountDetailDto>($"api/accounts/{id}");
    public Task<List<AccountEventDto>> EventsAsync(string id, long? before = null) => GetAsync<List<AccountEventDto>>($"api/accounts/{id}/events" + (before is null ? "" : $"?before={before}"));
    public Task<List<UserDto>> DirectoryAsync() => GetAsync<List<UserDto>>("api/directory/users");
    public Task<List<UserDto>> SharesAsync(string id) => GetAsync<List<UserDto>>($"api/accounts/{id}/shares");
    public Task<LinkResponse> BeginLinkAsync() => PostAsync<LinkResponse>("api/accounts/discord/link", new { });
    public Task<PendingResponse> LinkStatusAsync(string id) => GetAsync<PendingResponse>($"api/accounts/discord/link/{id}");
    public Task RefreshAsync(string id) => SendAsync(HttpMethod.Post, $"api/accounts/{id}/refresh");
    public Task DisconnectAsync(string id) => SendAsync(HttpMethod.Post, $"api/accounts/{id}/disconnect");
    public Task DeleteAsync(string id) => SendAsync(HttpMethod.Delete, $"api/accounts/{id}");
    public Task ShareAsync(string id, string userId) => SendAsync(HttpMethod.Put, $"api/accounts/{id}/shares/{userId}");
    public Task UnshareAsync(string id, string userId) => SendAsync(HttpMethod.Delete, $"api/accounts/{id}/shares/{userId}");
    public Task ChangePasswordAsync(string current, string next) => SendAsync(HttpMethod.Post, "api/auth/change-password", new { currentPassword = current, newPassword = next });
    public Task LogoutAsync() => SendAsync(HttpMethod.Post, "api/auth/logout");
    public Task<CreatedUserDto> CreateUserAsync(string username) => PostAsync<CreatedUserDto>("api/admin/users", new { username });
    public Task<ResetPasswordDto> ResetPasswordAsync(string id) => PostAsync<ResetPasswordDto>($"api/admin/users/{id}/reset-password", new { });

    private async Task<T> GetAsync<T>(string path)
    {
        using var response = await _http.GetAsync(path);
        await CheckAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await _http.PostAsJsonAsync(path, body, Json);
        await CheckAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request);
        await CheckAsync(response);
    }

    private static async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = $"API-Fehler {(int)response.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("error", out var error)) message = error.GetString() ?? message;
        }
        catch (JsonException) { }
        throw new InvalidOperationException(message);
    }

    public void Dispose() => _http.Dispose();
}

public sealed class UserDto
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool MustChangePassword { get; set; }
    public override string ToString() => Username;
}
public sealed class LoginResponse { public string Token { get; set; } = ""; public UserDto User { get; set; } = new(); }
public sealed class CreatedUserDto { public UserDto User { get; set; } = new(); public string TemporaryPassword { get; set; } = ""; }
public sealed class ResetPasswordDto { public string TemporaryPassword { get; set; } = ""; }
public sealed class LinkResponse { public string PendingId { get; set; } = ""; public string AuthorizationUrl { get; set; } = ""; }
public sealed class PendingResponse { public string Status { get; set; } = ""; public string? AccountId { get; set; } public string? ErrorMessage { get; set; } }
public sealed class AccountDto
{
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerUsername { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? LastError { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public bool IsEditor { get; set; }
    public string LastCheckedLocal => LastCheckedAt is null ? "–" : DateTime.SpecifyKind(LastCheckedAt.Value, DateTimeKind.Utc).ToLocalTime().ToString("g");
}
public sealed class AccountDetailDto { public AccountDto Account { get; set; } = new(); public SnapshotDto? Snapshot { get; set; } }
public sealed class SnapshotDto { public ProfileDto Profile { get; set; } = new(); public List<GuildDto> Guilds { get; set; } = []; }
public sealed class ProfileDto { public string Id { get; set; } = ""; public string Username { get; set; } = ""; public string DisplayName { get; set; } = ""; }
public sealed class GuildDto { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public override string ToString() => Name; }
public sealed class AccountEventDto
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public string Display => $"{DateTime.SpecifyKind(OccurredAt, DateTimeKind.Utc).ToLocalTime():g}  {Description}";
}
