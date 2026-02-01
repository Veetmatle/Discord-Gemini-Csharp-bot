namespace Discord_Bot_AI.Strategy.Notification;

/// <summary>
/// Interface defining the contract for match notification strategies.
/// </summary>
public interface IMatchNotification
{
    /// <summary>
    /// Begins monitoring for match completion events based on the strategy implementation.
    /// </summary>
    Task StartMonitoringAsync();
    
    /// <summary>
    /// Gracefully stops monitoring and releases resources.
    /// </summary>
    Task StopMonitoringAsync();
}