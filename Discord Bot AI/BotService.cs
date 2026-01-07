using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace Discord_Bot_AI;

public class BotService
{
    private readonly DiscordSocketClient _client;
    private Config? _config;
    
    // Those should be a list. But for now it's ok.
    private ulong _tafterServerGuildId;
    private ulong _myServerGuidId;
    private ulong _bambikServerGuildId;
    private readonly string _propmtPrefix =
        "\n Answer in Polish in max 100 words. Be brief and precise unless instructions say otherwise.";
    private GeminiService? _gemini;

    public BotService()
    {
        var config = new DiscordSocketConfig { GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent };
        _client = new DiscordSocketClient(config);
        _client.Ready += OnReadyAsync;
        _client.SlashCommandExecuted += OnSlashCommandAsync;
    }

    public async Task RunAsync()
    {
        await LoadConfigAsync();
        if (_config == null) return;

        _gemini = new GeminiService(_config.GeminiApiKey);
        _tafterServerGuildId = ulong.Parse(_config.ServerID_1);
        _myServerGuidId = ulong.Parse(_config.ServerID_2);
        _bambikServerGuildId = ulong.Parse(_config.ServerID_3);

        await _client.LoginAsync(TokenType.Bot, _config.DiscordToken);
        await _client.StartAsync();
        await Task.Delay(-1);
    }

    private async Task LoadConfigAsync()
    {
        if (!File.Exists("config.json"))
            throw new FileNotFoundException();

        string json = await File.ReadAllTextAsync("config.json");
        _config = JsonConvert.DeserializeObject<Config>(json)
                  ?? throw new Exception("Deserialization error.");
    }

    private async Task OnReadyAsync()
    {
        ulong[] guildIds = { _tafterServerGuildId, _myServerGuidId, _bambikServerGuildId };

        var command = new SlashCommandBuilder()
            .WithName("laskbot")
            .WithDescription("bot main command")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("ask")
                .WithDescription("write your question")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("query", ApplicationCommandOptionType.String, "here provide content", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("info")
                .WithDescription("show info about bot")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .Build();

        foreach (var id in guildIds)
        {
            var guild = _client.GetGuild(id);
            if (guild != null)
            {
                try
                {
                    await guild.CreateApplicationCommandAsync(command);
                    Console.WriteLine($"Registered for {guild.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error on {id}: {ex.Message}");
                }
            }
        }
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand command)
    {
        if (command.Data.Name != "laskbot") return;
        
        var subCommand = command.Data.Options.First();
        if (subCommand.Name == "ask")
        {
            await command.DeferAsync();

            var question = subCommand.Options.First().Value.ToString() ?? "Zwróć odpowiedź: Brak pytania";
            var answer = await _gemini!.GetAnswerAsync(question + this._propmtPrefix);

            Console.WriteLine(question);
            Console.WriteLine(answer);

            string response = $"**Question:**\n {question}\n**Answer:**\n {answer}";
            await command.FollowupAsync(response);
        }
        else if (subCommand.Name == "info")
        {
            string info = "LaskBot -> v1. Created by Lask. Use /laskbot ask to ask questions.";
            await command.RespondAsync(info);
        }
    }
}