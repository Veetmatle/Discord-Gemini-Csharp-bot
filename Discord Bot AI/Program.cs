namespace Discord_Bot_AI;

public class Program
{
    static async Task Main()
    {
        var botService = new BotService();
        await botService.RunAsync();
    }
}