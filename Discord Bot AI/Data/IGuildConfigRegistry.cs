using Discord_Bot_AI.Models;

namespace Discord_Bot_AI.Data;

/// <summary>
/// Interface for guild configuration operations.
/// Manages per-server settings like notification channels.
/// </summary>
public interface IGuildConfigRegistry : IDisposable
{
    /// <summary>
    /// Sets or updates the notification channel for a guild.
    /// </summary>
    /// <param name="guildId">The Discord guild ID.</param>
    /// <param name="channelId">The text channel ID for notifications.</param>
    void SetNotificationChannel(ulong guildId, ulong channelId);
    ulong? GetNotificationChannel(ulong guildId);
    void RemoveGuild(ulong guildId);
    GuildConfig? GetConfig(ulong guildId);
}
