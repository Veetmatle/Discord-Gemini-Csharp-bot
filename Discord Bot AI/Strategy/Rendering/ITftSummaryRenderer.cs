namespace Discord_Bot_AI.Strategy.Rendering;

using Models;

/// <summary>
/// Interface for rendering TFT match summary images.
/// </summary>
public interface ITftSummaryRenderer
{
    /// <summary>
    /// Renders a visual summary of a completed TFT match and returns it as a stream.
    /// </summary>
    Task<Stream> RenderTftSummaryAsync(RiotAccount account, TftMatchData matchData, CancellationToken cancellationToken = default);
}
