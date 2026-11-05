using System.Text.Json;
using LLMFriend.Core;
using LLMFriend.Infrastructure;

namespace LLMFriend.Api;

public static class ApiEndpoints
{
    public static void MapApi(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, UserService users, SessionService sessions) =>
        {
            var user = await users.VerifyAsync(request.Username, request.Password);
            if (user is null) return Results.Unauthorized();
            var token = await sessions.CreateAsync(user.Id);
            return Results.Ok(new { token, user = PublicUser(user) });
        }).RequireRateLimiting("login");
        app.MapGet("/api/auth/me", (HttpContext ctx) => Results.Ok(PublicUser(Actor(ctx))));
        app.MapPost("/api/auth/logout", async (HttpContext ctx, SessionService sessions) =>
        {
            await sessions.RevokeAsync(ctx.Request.Headers.Authorization.ToString()[7..]);
            return Results.NoContent();
        });
        app.MapPost("/api/auth/change-password", async (HttpContext ctx, ChangePasswordRequest request, UserService users) =>
        {
            var user = Actor(ctx);
            if (await users.VerifyAsync(user.Username, request.CurrentPassword) is null) return Results.Unauthorized();
            await users.ChangePasswordAsync(user, request.NewPassword);
            return Results.NoContent();
        });

        app.MapGet("/api/admin/users", async (HttpContext ctx, UserService users) =>
            Actor(ctx).IsAdmin ? Results.Ok((await users.ListAsync()).Select(PublicUser)) : Results.StatusCode(403));
        app.MapPost("/api/admin/users", async (HttpContext ctx, CreateUserRequest request, UserService users) =>
        {
            if (!Actor(ctx).IsAdmin) return Results.StatusCode(403);
            var (user, password) = await users.CreateAsync(request.Username, false);
            return Results.Created($"/api/admin/users/{user.Id}", new { user = PublicUser(user), temporaryPassword = password });
        });
        app.MapPost("/api/admin/users/{id}/reset-password", async (HttpContext ctx, string id, UserService users) =>
            Actor(ctx).IsAdmin ? Results.Ok(new { temporaryPassword = await users.ResetPasswordAsync(id) }) : Results.StatusCode(403));
        app.MapGet("/api/directory/users", async (UserService users) => Results.Ok((await users.ListAsync()).Select(u => new { u.Id, u.Username })));

        app.MapGet("/api/accounts", async (HttpContext ctx, AccountService accounts) =>
            Results.Ok((await accounts.ListVisibleAsync(Actor(ctx).Id)).Select(PublicAccount)));
        app.MapGet("/api/accounts/{id}", async (HttpContext ctx, string id, AccountService accounts) =>
        {
            var account = await accounts.GetVisibleAsync(id, Actor(ctx).Id);
            return account is null ? Results.NotFound() : Results.Ok(new { account = PublicAccount(account), snapshot = account.SnapshotJson is null ? null : JsonSerializer.Deserialize<ConnectorSnapshot>(account.SnapshotJson) });
        });
        app.MapPost("/api/accounts/discord/link", async (HttpContext ctx, AccountService accounts) =>
        {
            var (pendingId, url) = await accounts.BeginLinkAsync(Actor(ctx).Id);
            return Results.Ok(new { pendingId, authorizationUrl = url });
        });
        app.MapGet("/api/accounts/discord/link/{id}", async (HttpContext ctx, string id, AccountService accounts) =>
        {
            var pending = await accounts.GetPendingAsync(id, Actor(ctx).Id);
            if (pending is null) return Results.NotFound();
            var status = pending.CompletedAt is null ? (pending.ExpiresAt < DateTime.UtcNow ? "expired" : "pending") : pending.ErrorMessage is null ? "completed" : "error";
            return Results.Ok(new { status, pending.AccountId, pending.ErrorMessage });
        });
        app.MapGet("/api/accounts/discord/callback", async (HttpContext ctx, AccountService accounts) =>
        {
            var state = ctx.Request.Query["state"].ToString();
            var code = ctx.Request.Query["code"].ToString();
            if (string.IsNullOrWhiteSpace(state)) return Results.BadRequest("OAuth-State fehlt.");
            if (string.IsNullOrWhiteSpace(code))
            {
                await accounts.FailLinkAsync(state, "Discord-Autorisierung abgebrochen");
                return Results.Text("Discord-Verknüpfung abgebrochen. Dieses Fenster kann geschlossen werden.");
            }
            await accounts.CompleteLinkAsync(state, code, ctx.RequestAborted);
            return Results.Text("Discord-Konto verbunden. Dieses Fenster kann geschlossen werden.");
        });
        app.MapPost("/api/accounts/{id}/refresh", async (HttpContext ctx, string id, AccountService accounts) =>
        {
            var account = await accounts.GetVisibleAsync(id, Actor(ctx).Id);
            if (account is null) return Results.NotFound();
            if (account.Status == "disconnected") return Results.BadRequest(new { error = "Konto ist getrennt." });
            await accounts.RequestRefreshAsync(id);
            return Results.Accepted();
        });
        app.MapGet("/api/accounts/{id}/events", async (HttpContext ctx, string id, long? before, AccountService accounts) =>
            await accounts.GetVisibleAsync(id, Actor(ctx).Id) is null ? Results.NotFound() : Results.Ok(await accounts.EventsAsync(id, before)));
        app.MapGet("/api/accounts/{id}/shares", async (HttpContext ctx, string id, AccountService accounts) =>
            await accounts.GetVisibleAsync(id, Actor(ctx).Id) is null ? Results.NotFound() : Results.Ok((await accounts.SharesAsync(id)).Select(x => new { x.Id, x.Username })));
        app.MapPut("/api/accounts/{id}/shares/{userId}", async (HttpContext ctx, string id, string userId, AccountService accounts) =>
        {
            if (await accounts.GetVisibleAsync(id, Actor(ctx).Id) is null) return Results.NotFound();
            await accounts.ShareAsync(id, userId);
            return Results.NoContent();
        });
        app.MapDelete("/api/accounts/{id}/shares/{userId}", async (HttpContext ctx, string id, string userId, AccountService accounts) =>
        {
            if (await accounts.GetVisibleAsync(id, Actor(ctx).Id) is null) return Results.NotFound();
            await accounts.UnshareAsync(id, userId);
            return Results.NoContent();
        });
        app.MapPost("/api/accounts/{id}/disconnect", async (HttpContext ctx, string id, AccountService accounts) =>
        {
            var account = await accounts.GetVisibleAsync(id, Actor(ctx).Id);
            if (account is null) return Results.NotFound();
            await accounts.DisconnectAsync(account, ctx.RequestAborted);
            return Results.NoContent();
        });
        app.MapDelete("/api/accounts/{id}", async (HttpContext ctx, string id, AccountService accounts) =>
        {
            var account = await accounts.GetVisibleAsync(id, Actor(ctx).Id);
            if (account is null) return Results.NotFound();
            if (!AccountAccess.CanDelete(Actor(ctx).Id, account.OwnerId)) return Results.StatusCode(403);
            if (account.Status != "disconnected") await accounts.DisconnectAsync(account, ctx.RequestAborted);
            await accounts.DeleteAsync(id);
            return Results.NoContent();
        });
    }

    private static UserRow Actor(HttpContext context) => (UserRow)context.Items["user"]!;
    private static object PublicUser(UserRow user) => new { user.Id, user.Username, user.IsAdmin, user.MustChangePassword };
    private static object PublicAccount(AccountRow a) => new { a.Id, a.OwnerId, a.OwnerUsername, a.Provider, a.ExternalId, a.Username, a.DisplayName, a.AvatarUrl, a.Status, a.LastError, a.LastCheckedAt, a.NextCheckAt, a.CreatedAt, a.IsEditor };
}

public sealed record LoginRequest(string Username, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record CreateUserRequest(string Username);
