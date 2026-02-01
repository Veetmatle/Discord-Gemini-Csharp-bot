using Discord_Bot_AI.Models;
namespace Discord_Bot_AI.Data;

/// <summary>
/// Interface for user registry operations following Interface Segregation Principle.
/// Manages user-to-Riot-account mappings only.
/// </summary>
public interface IUserRegistry : IDisposable
{
    /// <summary>
    /// Registers a Riot account for a Discord user on a specific guild.
    /// If the account already exists, adds the guild to the list of registered guilds.
    /// </summary>
    void RegisterUser(ulong discordUserId, RiotAccount account, ulong guildId);
    
    /// <summary>
    /// Updates the last match ID for an existing account.
    /// </summary>
    void UpdateLastMatchId(ulong discordUserId, string lastMatchId);
    
    /// <summary>
    /// Removes a user's registration from a specific guild.
    /// If no guilds remain, removes the account entirely.
    /// </summary>
    bool RemoveUserFromGuild(ulong discordUserId, ulong guildId);
    RiotAccount? GetAccount(ulong discordUserId);
    
    /// <summary>
    /// Returns a snapshot of all tracked users.
    /// </summary>
    List<KeyValuePair<ulong, RiotAccount>> GetAllTrackedUsers();
}
