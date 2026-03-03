namespace Discord_Bot_AI.Services.Agent;

/// <summary>
/// High-level facade for agent task operations.
/// BotService depends on this interface, not on concrete implementations.
/// </summary>
public interface IAgentService
{
    Task<string> SubmitTaskAsync(
        ulong discordUserId,
        ulong guildId,
        ulong channelId,
        string prompt,
        Stream? pdfAttachment,
        string? pdfFileName,
        CancellationToken cancellationToken = default);
    
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    Func<Models.AgentTaskResult, Models.AgentTask, CancellationToken, Task>? OnTaskCompleted { get; set; }
}
