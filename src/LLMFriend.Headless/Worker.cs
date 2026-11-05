using LLMFriend.Infrastructure;

namespace LLMFriend.Headless;

public sealed class Worker(IServiceScopeFactory scopes, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
                var monitor = scope.ServiceProvider.GetRequiredService<MonitorService>();
                foreach (var account in await accounts.DueAsync())
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await monitor.CheckAsync(account.Id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Monitoring-Durchlauf fehlgeschlagen"); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
