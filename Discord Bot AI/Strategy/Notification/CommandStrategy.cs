using Discord_Bot_AI.Services;
using Discord_Bot_AI.Models;
using Discord_Bot_AI.Data;

namespace Discord_Bot_AI.Strategy.Notification;

public class CommandStrategy : IMatchNotification
{
    private readonly RiotService _riot;
    private readonly UserRegistry _userRegistry;
    private readonly Func<RiotAccount, MatchData, Task> _onMatchFound;

    public CommandStrategy(RiotService riot, UserRegistry registry, Func<RiotAccount, MatchData, Task> onMatchFound)
    {
        _riot = riot;
        _userRegistry = registry;
        _onMatchFound = onMatchFound;
    }
    
    /// <summary>
    /// Command strategy doesn't require background monitoring.
    /// </summary>
    public Task StartMonitoringAsync() => Task.CompletedTask;
    
    /// <summary>
    /// Checks and retrieves the latest match data for a specific Discord user on demand.
    /// </summary>
    public async Task CheckUserMatchAsync(ulong discordId)
    {
        var account = _userRegistry.GetAccount(discordId);
        if (account == null) return;

        string? currentMatchId = await _riot.GetLatestMatchIdAsync(account.puuid);
        
        if (!string.IsNullOrEmpty(currentMatchId))
        {
            var matchData = await _riot.GetMatchDetailsAsync(currentMatchId);
            if (matchData != null)
            {
                await _onMatchFound(account, matchData);
            }
        }
    }
}