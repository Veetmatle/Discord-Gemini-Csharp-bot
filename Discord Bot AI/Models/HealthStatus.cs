namespace Discord_Bot_AI.Models;

/// <summary>
/// Represents the health status of the bot for monitoring and Docker health checks.
/// </summary>
public class HealthStatus
{
    /// <summary>
    /// Indicates whether the bot is in a healthy operational state.
    /// </summary>
    public bool IsHealthy { get; set; }
    
    /// <summary>
    /// The total time the bot has been running since startup.
    /// </summary>
    public TimeSpan Uptime { get; set; }
    
    /// <summary>
    /// The current Discord connection state.
    /// </summary>
    public string ConnectionState { get; set; } = string.Empty;
    
    /// <summary>
    /// The number of users currently being tracked for match notifications.
    /// </summary>
    public int TrackedUsersCount { get; set; }
    
    /// <summary>
    /// Indicates if the Riot API service is currently rate-limited.
    /// </summary>
    public bool RiotApiRateLimited { get; set; }
    
    /// <summary>
    /// Indicates if the Gemini API service is currently rate-limited.
    /// </summary>
    public bool GeminiApiRateLimited { get; set; }
}
