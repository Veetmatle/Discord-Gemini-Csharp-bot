namespace Discord_Bot_AI.Configuration;
/// <summary>
/// Application settings container loaded from environment variables or config file.
/// Immutable after creation for thread safety.
/// </summary>
public sealed class AppSettings
{
    public string DiscordToken { get; init; } = string.Empty;
    public string GeminiApiKey { get; init; } = string.Empty;
    public string RiotToken { get; init; } = string.Empty;
    public string RiotVersion { get; init; } = string.Empty;
    public string DataPath { get; init; } = "/app/data";
    public string CachePath { get; init; } = "/app/cache";
    public string LogPath { get; init; } = "/app/logs";
    /// <summary>
    /// Creates AppSettings from an IConfigurationProvider.
    /// </summary>
    public static AppSettings FromProvider(IConfigurationProvider provider)
    {
        return new AppSettings
        {
            DiscordToken = provider.GetRequiredValue("DISCORD_TOKEN"),
            GeminiApiKey = provider.GetRequiredValue("GEMINI_API_KEY"),
            RiotToken = provider.GetRequiredValue("RIOT_TOKEN"),
            RiotVersion = provider.GetValue("RIOT_VERSION") ?? "14.2.1",
            DataPath = provider.GetValue("DATA_PATH") ?? "/app/data",
            CachePath = provider.GetValue("CACHE_PATH") ?? "/app/cache",
            LogPath = provider.GetValue("LOG_PATH") ?? "/app/logs"
        };
    }
}
