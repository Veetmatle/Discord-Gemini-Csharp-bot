using Discord_Bot_AI.Models;

namespace Discord_Bot_AI.Services.Agent;

/// <summary>
/// Abstracts communication with the external AI agent container (e.g. OpenClaw).
/// Allows swapping agent backends without modifying orchestration logic.
/// </summary>
public interface IAgentClient
{
    Task<string> SubmitTaskAsync(AgentApiRequest request, CancellationToken cancellationToken = default);
    Task<AgentApiResponse?> GetTaskStatusAsync(string taskId, CancellationToken cancellationToken = default);
    Task CancelTaskAsync(string taskId, CancellationToken cancellationToken = default);
}
