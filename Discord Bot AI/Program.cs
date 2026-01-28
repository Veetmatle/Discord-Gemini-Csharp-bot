﻿using Discord_Bot_AI.Services;

namespace Discord_Bot_AI;

public class Program
{
    /// <summary>
    /// Entry point of the application, initializes and starts the bot service.
    /// </summary>
    static async Task Main()
    {
        var botService = new BotService();
        await botService.RunAsync();
    }
}