using Discord_Bot_AI.Data;
using Discord;
using Discord.WebSocket;
using Discord_Bot_AI.Models;
using Discord_Bot_AI.Configuration;
using Discord_Bot_AI.Strategy.Notification;
using Discord_Bot_AI.Strategy.Rendering;
using Serilog;

namespace Discord_Bot_AI.Services;

/// <summary>
/// Main orchestrator service for the Discord bot, managing all subsystems and their lifecycle.
/// Implements IAsyncDisposable for graceful shutdown support (Docker-friendly).
/// </summary>
public class BotService : IAsyncDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly AppSettings _settings;
    private GeminiService? _gemini;
    private RiotService? _riot;
    private RiotImageCacheService? _imageCache;
    private ImageSharpRenderer? _renderer;
    private readonly List<ulong> _guildIds = new();
    private IUserRegistry? _userRegistry;
    private readonly List<IMatchNotification> _notificationStrategies = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private DateTime _startTime;
    private bool _disposed;

    /// <summary>
    /// Creates a new BotService with the provided settings.
    /// </summary>
    /// <param name="settings">Application settings loaded from environment or config.</param>
    public BotService(AppSettings settings)
    {
        _settings = settings;
        var config = new DiscordSocketConfig { GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent };
        _client = new DiscordSocketClient(config);
        _client.Ready += OnReadyAsync;
        _client.SlashCommandExecuted += OnSlashCommandAsync;
        _client.Log += OnDiscordLogAsync;
    }

    /// <summary>
    /// Starts the bot and all its subsystems. Blocks until shutdown is requested.
    /// </summary>
    public async Task RunAsync()
    {
        _startTime = DateTime.UtcNow;
        
        ValidateSettings();

        InitializeCoreServices();
        InitializeRenderingSystem();
        InitializeNotificationStrategies();
        ParseGuildIds();

        RegisterShutdownHandlers();

        await StartDiscordClientAsync();
        
        StartBackgroundMonitoring();
        
        Log.Information("Bot started successfully. Uptime tracking started.");

        try
        {
            await Task.Delay(Timeout.Infinite, _shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Shutdown signal received");
        }
        
        await ShutdownAsync();
    }

    /// <summary>
    /// Validates the application settings.
    /// </summary>
    private void ValidateSettings()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(_settings.DiscordToken))
            errors.Add("DISCORD_TOKEN is required");
        if (string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
            errors.Add("GEMINI_API_KEY is required");
        if (string.IsNullOrWhiteSpace(_settings.RiotToken))
            errors.Add("RIOT_TOKEN is required");
        if (_settings.ServerIds.Count == 0)
            errors.Add("SERVER_IDS is required (comma-separated)");
            
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                Log.Error("Configuration error: {Error}", error);
            throw new InvalidOperationException($"Configuration validation failed with {errors.Count} error(s)");
        }
        
        Log.Information("Configuration validated successfully");
    }

    /// <summary>
    /// Returns the current health status of the bot for monitoring purposes.
    /// </summary>
    public HealthStatus GetHealthStatus()
    {
        return new HealthStatus
        {
            IsHealthy = _client.ConnectionState == ConnectionState.Connected,
            Uptime = DateTime.UtcNow - _startTime,
            ConnectionState = _client.ConnectionState.ToString(),
            TrackedUsersCount = _userRegistry?.GetAllTrackedUsers().Count ?? 0,
            RiotApiRateLimited = _riot?.IsRateLimited ?? false,
            GeminiApiRateLimited = _gemini?.IsRateLimited ?? false
        };
    }

    /// <summary>
    /// Handles Discord.NET log messages and forwards them to Serilog.
    /// </summary>
    private Task OnDiscordLogAsync(LogMessage msg)
    {
        var severity = msg.Severity switch
        {
            LogSeverity.Critical => Serilog.Events.LogEventLevel.Fatal,
            LogSeverity.Error => Serilog.Events.LogEventLevel.Error,
            LogSeverity.Warning => Serilog.Events.LogEventLevel.Warning,
            LogSeverity.Info => Serilog.Events.LogEventLevel.Information,
            LogSeverity.Verbose => Serilog.Events.LogEventLevel.Verbose,
            LogSeverity.Debug => Serilog.Events.LogEventLevel.Debug,
            _ => Serilog.Events.LogEventLevel.Information
        };
        
        Log.Write(severity, msg.Exception, "[Discord] {Message}", msg.Message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers handlers for OS shutdown signals (SIGTERM, SIGINT, etc.).
    /// </summary>
    private void RegisterShutdownHandlers()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Log.Information("SIGINT received, initiating shutdown...");
            _shutdownCts.Cancel();
        };
        
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log.Information("Process exit detected, initiating shutdown...");
            _shutdownCts.Cancel();
        };
    }

    /// <summary>
    /// Gracefully shuts down all bot subsystems.
    /// </summary>
    private async Task ShutdownAsync()
    {
        Log.Information("Shutting down...");
        
        var stopTasks = _notificationStrategies.Select(s => s.StopMonitoringAsync()).ToList();
        await Task.WhenAll(stopTasks);
        
        await _client.StopAsync();
        await _client.LogoutAsync();
        
        Log.Information("Shutdown complete");
    }

    /// <summary>
    /// Initializes core API services (Gemini AI, Riot API).
    /// </summary>
    private void InitializeCoreServices()
    {
        _userRegistry = new UserRegistry(_settings.DataPath);
        _gemini = new GeminiService(_settings.GeminiApiKey);
        _riot = new RiotService(_settings.RiotToken);
        Log.Information("Core services initialized");
    }

    /// <summary>
    /// Initializes the rendering system for match summaries.
    /// </summary>
    private void InitializeRenderingSystem()
    {
        _imageCache = new RiotImageCacheService(_settings.RiotVersion, _settings.CachePath);
        _renderer = new ImageSharpRenderer(_imageCache);
        Log.Information("Rendering system initialized");
    }

    /// <summary>
    /// Initializes notification strategies for match detection.
    /// </summary>
    private void InitializeNotificationStrategies()
    {
        _notificationStrategies.Add(new PollingStrategy(_riot!, _userRegistry!, NotifyMatchFinishedAsync));
        _notificationStrategies.Add(new CommandStrategy(_riot!, _userRegistry!, NotifyMatchFinishedAsync));
        Log.Information("Notification strategies initialized: {Count}", _notificationStrategies.Count);
    }

    /// <summary>
    /// Parses guild IDs from configuration.
    /// </summary>
    private void ParseGuildIds()
    {
        foreach (var id in _settings.ServerIds)
        {
            if (ulong.TryParse(id, out ulong guildId))
            {
                _guildIds.Add(guildId);
            }
        }
    }

    private async Task StartDiscordClientAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _settings.DiscordToken);
        await _client.StartAsync();
    }

    private void StartBackgroundMonitoring()
    {
        foreach (var strategy in _notificationStrategies)
        {
            _ = strategy.StartMonitoringAsync();
        }
    }

    private async Task OnReadyAsync()
    {
        var command = new SlashCommandBuilder()
            .WithName("laskbot")
            .WithDescription("bot main command")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("ask")
                .WithDescription("write your question")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("query", ApplicationCommandOptionType.String, "here provide content", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("info")
                .WithDescription("show info about bot")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("unregister")
                .WithDescription("Use to unregister your League of Legends account")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder() 
                .WithName("register")
                .WithDescription("Register your League of Legends account")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("nick", ApplicationCommandOptionType.String, "Your nick in game", isRequired: true)
                .AddOption("tag", ApplicationCommandOptionType.String, "Your tag (eg. EUNE, PL1)", isRequired: true))
            .Build();

        foreach (var id in _guildIds)
        {
            var guild = _client.GetGuild(id);
            if (guild != null)
            {
                try
                {
                    await guild.CreateApplicationCommandAsync(command);
                    Console.WriteLine($"Registered for {guild.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error on {id}: {ex.Message}");
                }
            }
        }
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand command)
    {
        if (command.Data.Name != "laskbot") return;
        
        var subCommand = command.Data.Options.First();
        switch (subCommand.Name)
        {
            case "ask":
                await HandleAskCommandAsync(command, subCommand); 
                break;
            case "info":
                await command.RespondAsync("LaskBot -> v1. Created by Lask.");
                break;
            case "register":
                await RegisterRiotAccountAsync(command, subCommand); 
                break;
            case "unregister":
                await UnregisterRiotAccountAsync(command);
                break;
        }
    }
    
    private async Task UnregisterRiotAccountAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);
        bool removed = _userRegistry.RemoveUser(command.User.Id);
        if (removed)
        {
            await command.FollowupAsync("Your account has been unregistered successfully.");
        }
        else
        {
            await command.FollowupAsync("No corresponding account found to unregister.");
        }
    }

    private async Task HandleAskCommandAsync(SocketSlashCommand command, SocketSlashCommandDataOption subCommand)
    {
        await command.DeferAsync();

        var queryOption = subCommand.Options.FirstOrDefault(o => o.Name == "query");
        var question = queryOption?.Value?.ToString() ?? "No question provided";
        var answer = await _gemini!.GetAnswerAsync(question);

        string response = $"**Question:**\n {question}\n**Answer:**\n {answer}";
        await command.FollowupAsync(response);
    }

    private async Task RegisterRiotAccountAsync(SocketSlashCommand command, SocketSlashCommandDataOption subCommand)
    {
        if (_riot == null) return;
        
        await command.DeferAsync();
        
        var nick = subCommand.Options.FirstOrDefault(o => o.Name == "nick")?.Value?.ToString();
        var tag = subCommand.Options.FirstOrDefault(o => o.Name == "tag")?.Value?.ToString();

        if (string.IsNullOrEmpty(nick) || string.IsNullOrEmpty(tag))
        {
            await command.FollowupAsync("Invalid nick or tag.");
            return;
        }
        
        var account = await _riot.GetAccountAsync(nick, tag);
        if (account != null)
        {
            account.LastMatchId = await _riot.GetLatestMatchIdAsync(account.puuid);
            _userRegistry.RegisterUser(command.User.Id, account);
            await command.FollowupAsync($"Account registered: **{account.gameName}#{account.tagLine}**.");
        }
        else
        {
            await command.FollowupAsync($"Account not found: **{nick}#{tag}**.");
        }
    }
    
    /// <summary>
    /// Method to notify a Discord channel when a match is finished.
    /// </summary>
    private async Task NotifyMatchFinishedAsync(RiotAccount account, MatchData matchData)
    {
        var guild = _client.GetGuild(_guildIds.FirstOrDefault()); 
        var channel = guild?.TextChannels.FirstOrDefault(c => c.Name == "bot"); 

        if (channel != null && _renderer != null)
        {
            using var imageStream = await _renderer.RenderSummaryAsync(account, matchData);
            await channel.SendFileAsync(imageStream, "match.png", $"**{account.gameName}** finished a match!");
        }
    }

    /// <summary>
    /// Releases all resources used by the bot service.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        
        _gemini?.Dispose();
        _riot?.Dispose();
        _imageCache?.Dispose();
        _renderer?.Dispose();
        _userRegistry?.Dispose();
        
        await _client.DisposeAsync();
        
        _disposed = true;
        Log.Debug("BotService disposed");
        GC.SuppressFinalize(this);
    }
}