using LLMFriend.Core;
using LLMFriend.Headless;
using LLMFriend.Infrastructure;

var mode = args.Length == 0 ? "worker" : args[0].ToLowerInvariant();
var builder = Host.CreateApplicationBuilder();
builder.Services.AddWindowsService();
builder.Services.AddSystemd();
builder.Services.AddSingleton(new Database(Environment.GetEnvironmentVariable("LLMFRIEND_DB") ?? ""));
builder.Services.AddSingleton(new TokenCipher(Environment.GetEnvironmentVariable("LLMFRIEND_ENCRYPTION_KEY") ?? ""));
builder.Services.AddSingleton(DiscordOptions.FromEnvironment());
builder.Services.AddHttpClient<IAccountConnector, DiscordConnector>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<MonitorService>();
if (mode == "worker") builder.Services.AddHostedService<Worker>();
using var host = builder.Build();

switch (mode)
{
    case "worker":
        await host.RunAsync();
        break;
    case "db" when args.Length == 2 && args[1] == "migrate":
        await host.Services.GetRequiredService<Database>().MigrateAsync();
        Console.WriteLine("Datenbankmigration abgeschlossen.");
        break;
    case "admin" when args.Length == 3 && args[1] == "bootstrap":
    {
        var users = host.Services.GetRequiredService<UserService>();
        if (await users.HasAdminAsync()) throw new InvalidOperationException("Ein Administrator existiert bereits.");
        var (user, password) = await users.CreateAsync(args[2], true);
        Console.WriteLine($"Administrator {user.Username} angelegt. Einmaliges Startpasswort: {password}");
        break;
    }
    case "admin" when args.Length == 3 && args[1] == "create-user":
    {
        var (user, password) = await host.Services.GetRequiredService<UserService>().CreateAsync(args[2], false);
        Console.WriteLine($"Nutzer {user.Username} angelegt. Einmaliges Startpasswort: {password}");
        break;
    }
    case "admin" when args.Length == 3 && args[1] == "reset-password":
    {
        var password = await host.Services.GetRequiredService<UserService>().ResetPasswordAsync(args[2]);
        Console.WriteLine($"Einmaliges neues Startpasswort: {password}");
        break;
    }
    case "monitor" when args.Length == 3 && args[1] == "once":
        await host.Services.GetRequiredService<MonitorService>().CheckAsync(args[2], CancellationToken.None);
        Console.WriteLine("Kontoprüfung abgeschlossen; Status in der Datenbank prüfen.");
        break;
    default:
        Console.Error.WriteLine("Aufruf: worker | db migrate | admin bootstrap <name> | admin create-user <name> | admin reset-password <user-id> | monitor once <account-id>");
        Environment.ExitCode = 2;
        break;
}
