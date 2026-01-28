namespace Discord_Bot_AI.Strategy.Rendering;

using Models;

public interface IGameSummaryRenderer
{
    /// <summary>
    /// Renders a visual summary of a completed match and returns it as a stream.
    /// </summary>
    Task<Stream> RenderSummaryAsync(RiotAccount account, MatchData matchData);
}