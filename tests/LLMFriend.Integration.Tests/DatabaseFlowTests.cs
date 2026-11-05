using Dapper;
using LLMFriend.Core;
using LLMFriend.Infrastructure;

namespace LLMFriend.Integration.Tests;

public sealed class DatabaseFlowTests
{
    [Fact]
    public async Task MigrationAuthenticationIsolationAndTokenRotationWork()
    {
        var connectionString = Environment.GetEnvironmentVariable("LLMFRIEND_TEST_DB")
            ?? throw new InvalidOperationException("LLMFRIEND_TEST_DB muss auf eine isolierte Testdatenbank zeigen.");
        var db = new Database(connectionString);
        await db.MigrateAsync();
        await db.MigrateAsync();
        await using (var connection = db.Open())
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=1"));

        var cipher = new TokenCipher(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        var connector = new FakeConnector();
        var users = new UserService(db);
        var sessions = new SessionService(db);
        var accounts = new AccountService(db, cipher, connector);
        var owner = await users.CreateAsync("owner_" + Guid.NewGuid().ToString("N")[..10], false);
        var editor = await users.CreateAsync("editor_" + Guid.NewGuid().ToString("N")[..10], false);
        var stranger = await users.CreateAsync("other_" + Guid.NewGuid().ToString("N")[..10], false);
        try
        {
            Assert.NotNull(await users.VerifyAsync(owner.User.Username, owner.Password));
            var session = await sessions.CreateAsync(owner.User.Id);
            Assert.NotNull(await sessions.AuthenticateAsync(session));
            await users.ChangePasswordAsync(owner.User, "new-password-long-enough");
            Assert.Null(await sessions.AuthenticateAsync(session));
            Assert.Null(await users.VerifyAsync(owner.User.Username, owner.Password));

            var link = await accounts.BeginLinkAsync(owner.User.Id);
            var state = Uri.UnescapeDataString(new Uri(link.Url).Query[7..]);
            await accounts.CompleteLinkAsync(state, "test-code", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.CompleteLinkAsync(state, "test-code", CancellationToken.None));
            var linked = Assert.Single(await accounts.ListVisibleAsync(owner.User.Id));
            Assert.Empty(await accounts.ListVisibleAsync(editor.User.Id));
            await accounts.ShareAsync(linked.Id, editor.User.Id);
            Assert.Single(await accounts.ListVisibleAsync(editor.User.Id));
            Assert.Empty(await accounts.ListVisibleAsync(stranger.User.Id));

            await using (var connection = db.Open())
            {
                var encrypted = await connection.ExecuteScalarAsync<string>("SELECT encrypted_access_token FROM accounts WHERE id=@id", new { linked.Id });
                Assert.NotNull(encrypted);
                Assert.DoesNotContain("access-initial", encrypted);
                Assert.Equal("access-initial", cipher.Decrypt(encrypted));
            }
            await new MonitorService(accounts, cipher, connector).CheckAsync(linked.Id, CancellationToken.None);
            Assert.True(connector.RefreshCalled);
            var events = await accounts.EventsAsync(linked.Id, null);
            Assert.Contains(events, x => x.Kind == "guild_joined");
            await using (var connection = db.Open())
            {
                var encrypted = await connection.ExecuteScalarAsync<string>("SELECT encrypted_refresh_token FROM accounts WHERE id=@id", new { linked.Id });
                Assert.NotNull(encrypted);
                Assert.Equal("refresh-rotated", cipher.Decrypt(encrypted));
            }
        }
        finally
        {
            await using var connection = db.Open();
            await connection.ExecuteAsync("DELETE FROM app_users WHERE id IN @ids", new { ids = new[] { owner.User.Id, editor.User.Id, stranger.User.Id } });
        }
    }

    private sealed class FakeConnector : IAccountConnector
    {
        public string Provider => "discord";
        public bool RefreshCalled { get; private set; }
        public string CreateAuthorizationUrl(string state, string codeChallenge) => "https://example.invalid?state=" + Uri.EscapeDataString(state);
        public Task<OAuthTokens> ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken) =>
            Task.FromResult(new OAuthTokens("access-initial", "refresh-initial", DateTime.UtcNow.AddMinutes(-1)));
        public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RefreshCalled = true;
            return Task.FromResult(new OAuthTokens("access-rotated", "refresh-rotated", DateTime.UtcNow.AddDays(1)));
        }
        public Task<ConnectorSnapshot> ReadSnapshotAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult(new ConnectorSnapshot(new ExternalProfile("discord-user-1", "test", "Test", null),
                accessToken == "access-initial" ? [] : [new ExternalGuild("guild-1", "Testserver", null)]));
        public Task RevokeAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
