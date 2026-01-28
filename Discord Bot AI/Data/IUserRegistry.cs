using Discord_Bot_AI.Models;
namespace Discord_Bot_AI.Data;

/// <summary>
/// Interface for user registry operations following Interface Segregation Principle.
/// </summary>
public interface IUserRegistry : IDisposable
{
    void RegisterUser(ulong discordUserId, RiotAccount account);
    void UpdateLastMatchId(ulong discordUserId, string lastMatchId);
    bool RemoveUser(ulong discordUserId);
    RiotAccount? GetAccount(ulong discordUserId);
    List<KeyValuePair<ulong, RiotAccount>> GetAllTrackedUsers();
}
