using Discord_Bot_AI.Models;

namespace Discord_Bot_AI.Strategy.Rendering;

public class HtmlRenderer : IGameSummaryRenderer
{
    public Task<Stream> RenderSummaryAsync(RiotAccount account, MatchData matchData)
    {
        throw new NotImplementedException();
    }
}