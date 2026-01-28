namespace Discord_Bot_AI.Strategy.Notification;

public interface IMatchNotification
{
    /// <summary>
    /// Begins monitoring for match completion events based on the strategy implementation.
    /// </summary>
    Task StartMonitoringAsync();
}