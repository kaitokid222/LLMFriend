using LLMFriend.Api;
using LLMFriend.Core;
using LLMFriend.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new Database(Environment.GetEnvironmentVariable("LLMFRIEND_DB") ?? ""));
builder.Services.AddSingleton(new TokenCipher(Environment.GetEnvironmentVariable("LLMFRIEND_ENCRYPTION_KEY") ?? ""));
builder.Services.AddSingleton(DiscordOptions.FromEnvironment());
builder.Services.AddHttpClient<IAccountConnector, DiscordConnector>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});

var app = builder.Build();
app.UseRouting();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    try
    {
        if (context.Request.Path.StartsWithSegments("/api") &&
            !context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) &&
            !context.Request.Path.Equals("/api/accounts/discord/callback", StringComparison.OrdinalIgnoreCase))
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 401;
                return;
            }
            var user = await context.RequestServices.GetRequiredService<SessionService>().AuthenticateAsync(authorization[7..]);
            if (user is null) { context.Response.StatusCode = 401; return; }
            context.Items["user"] = user;
            if (user.MustChangePassword && context.Request.Path != "/api/auth/change-password" && context.Request.Path != "/api/auth/logout" && context.Request.Path != "/api/auth/me")
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "Passwortwechsel erforderlich" });
                return;
            }
        }
        await next(context);
    }
    catch (ArgumentException ex) { await WriteError(context, 400, ex.Message); }
    catch (KeyNotFoundException ex) { await WriteError(context, 404, ex.Message); }
    catch (InvalidOperationException ex) { await WriteError(context, 400, ex.Message); }
    catch (MySqlConnector.MySqlException ex) when (ex.Number == 1062) { await WriteError(context, 409, "Eintrag existiert bereits."); }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "API-Fehler");
        await WriteError(context, 500, "Interner Serverfehler");
    }
});

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (Database db) =>
{
    await using var connection = db.Open();
    await connection.OpenAsync();
    return Results.Ok(new { status = "ready" });
});
app.MapApi();
app.Run();

static async Task WriteError(HttpContext context, int status, string message)
{
    if (context.Response.HasStarted) return;
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}
