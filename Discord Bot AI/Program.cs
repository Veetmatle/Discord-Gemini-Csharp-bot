using Discord_Bot_AI.Services;
using Discord_Bot_AI.Configuration;
using Serilog;

namespace Discord_Bot_AI;

public class Program
{
    /// <summary>
    /// Entry point of the application. Initializes logging and starts the bot service.
    /// </summary>
    static async Task Main()
    {
        var configProvider = new EnvironmentConfigProvider();
        var logPath = configProvider.GetValue("LOG_PATH") ?? "logs";
        
        LoggingService.Initialize(logPath);
        
        try
        {
            Log.Information("Starting Discord Bot...");
            Log.Information("Environment: Docker={IsDocker}", Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true");
            
            var settings = AppSettings.FromProvider(configProvider);
            
            await using var botService = new BotService(settings);
            await botService.RunAsync();
            
            Log.Information("Discord Bot has exited");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            Environment.ExitCode = 1;
        }
        finally
        {
            LoggingService.Shutdown();
        }
    }
}