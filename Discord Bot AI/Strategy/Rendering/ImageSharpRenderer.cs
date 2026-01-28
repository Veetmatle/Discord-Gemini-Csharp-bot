using Discord_Bot_AI.Models;
using Discord_Bot_AI.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using Serilog;

namespace Discord_Bot_AI.Strategy.Rendering;

/// <summary>
/// Renders match summary images using ImageSharp library with concurrency control and timeout protection.
/// </summary>
public class ImageSharpRenderer : IGameSummaryRenderer, IDisposable
{
    private readonly FontCollection _fontCollection = new();
    private readonly FontFamily _headingFamily;
    private readonly FontFamily _statsFamily;
    private readonly RiotImageCacheService _imageCache;
    
    private readonly SemaphoreSlim _renderQueue = new(2, 2);
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(30);
    private bool _disposed;

    /// <summary>
    /// Initializes the renderer, loads font collections from assets, and injects the image cache service.
    /// </summary>
    public ImageSharpRenderer(RiotImageCacheService imageCache)
    {
        _imageCache = imageCache;
        _headingFamily = _fontCollection.Add("Assets/Fonts/Cinzel static/Cinzel-Bold.ttf");
        _statsFamily = _fontCollection.Add("Assets/Fonts/Roboto static/RobotoCondensed-Bold.ttf");
    }

    /// <summary>
    /// Main method to process match data. Prepares graphical assets asynchronously 
    /// and generates the final summary image as a stream with timeout protection.
    /// </summary>
    public async Task<Stream> RenderSummaryAsync(RiotAccount account, MatchData matchData)
    {
        using var timeoutCts = new CancellationTokenSource(RenderTimeout);
        
        if (!await _renderQueue.WaitAsync(RenderTimeout))
        {
            Log.Warning("Render queue full, request timed out waiting for slot");
            throw new TimeoutException("Render queue is full. Please try again later.");
        }
        
        try
        {
            return await RenderSummaryInternalAsync(account, matchData, timeoutCts.Token);
        }
        finally
        {
            _renderQueue.Release();
        }
    }

    /// <summary>
    /// Internal rendering logic with cancellation support.
    /// </summary>
    private async Task<Stream> RenderSummaryInternalAsync(RiotAccount account, MatchData matchData, CancellationToken cancellationToken)
    {
        var me = matchData.info.participants.FirstOrDefault(p => p.puuid == account.puuid);
        if (me == null) throw new Exception("Player not found in match data");

        cancellationToken.ThrowIfCancellationRequested();
        
        string championPath = await _imageCache.GetChampionIconAsync(me.championName);
    
        var itemPaths = new List<string>();
        int[] itemIds = { me.item0, me.item1, me.item2, me.item3, me.item4, me.item5, me.item6 };
        
        var itemTasks = itemIds.Select(id => _imageCache.GetItemIconAsync(id)).ToList();
        var results = await Task.WhenAll(itemTasks);
        itemPaths.AddRange(results);

        cancellationToken.ThrowIfCancellationRequested();

        using Image<Rgba32> image = new Image<Rgba32>(1100, 350);

        image.Mutate(ctx =>
        {
            DrawBackground(ctx);
            DrawHeader(ctx, me, matchData);
            DrawTableHeaders(ctx);
            DrawPlayerRow(ctx, me, 160, championPath, itemPaths);
        });

        var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms, cancellationToken);
        ms.Position = 0;
        
        Log.Debug("Rendered match summary for {PlayerName}", account.gameName);
        return ms;
    }

    /// <summary>
    /// Fills the entire canvas with the base background color.
    /// </summary>
    private void DrawBackground(IImageProcessingContext ctx)
    {
        ctx.Fill(Color.FromRgb(10, 20, 25));
    }

    /// <summary>
    /// Draws the image header containing match status (Victory/Defeat) and game metadata.
    /// </summary>
    private void DrawHeader(IImageProcessingContext ctx, Participant me, MatchData matchData)
    {
        var titleFont = _headingFamily.CreateFont(42);
        var subFont = _statsFamily.CreateFont(16);

        string title = me.win ? "ZWYCIĘSTWO" : "PORAŻKA";
        Color titleColor = me.win ? Color.FromRgb(200, 170, 110) : Color.FromRgb(220, 50, 50);

        ctx.DrawText(title, titleFont, titleColor, new PointF(40, 30));

        string gameInfo = $"Summoner's Rift • Ranked Solo/Duo • {matchData.info.gameDuration / 60} min";
        ctx.DrawText(gameInfo, subFont, Color.Gray, new PointF(40, 85));
    }

    /// <summary>
    /// Draws the column names for statistics in the results table.
    /// </summary>
    private void DrawTableHeaders(IImageProcessingContext ctx)
    {
        var headerFont = _statsFamily.CreateFont(14);
        Color headerColor = Color.FromRgb(100, 120, 130);

        ctx.DrawText("TABLICA WYNIKÓW", headerFont, headerColor, new PointF(40, 125));
        ctx.DrawText("K / D / A", headerFont, headerColor, new PointF(680, 125));
        ctx.DrawText("CS", headerFont, headerColor, new PointF(850, 125));
        ctx.DrawText("GOLD", headerFont, headerColor, new PointF(950, 125));
    }

    /// <summary>
    /// Draws the full player data row, including champion icon, items, and numerical statistics.
    /// </summary>
    private void DrawPlayerRow(IImageProcessingContext ctx, Participant me, float yOffset, string champPath, List<string> itemPaths)
    {
        var nameFont = _statsFamily.CreateFont(20);
        var kdaFont = _statsFamily.CreateFont(22);
        var goldColor = Color.FromRgb(200, 170, 110);

        if (!string.IsNullOrEmpty(champPath))
        {
            using var champImg = Image.Load(champPath);
            champImg.Mutate(i => i.Resize(60, 60));
            ctx.DrawImage(champImg, new Point(40, (int)yOffset), 1f);
        }

        ctx.DrawText(me.summonerName, nameFont, goldColor, new PointF(115, yOffset + 18));

        DrawItems(ctx, itemPaths, new PointF(350, yOffset + 10));

        string kda = $"{me.kills} / {me.deaths} / {me.assists}";
        ctx.DrawText(kda, kdaFont, goldColor, new PointF(700, yOffset + 15));

        ctx.DrawText(me.totalMinionsKilled.ToString(), nameFont, Color.White, new PointF(860, yOffset + 18));

        ctx.DrawText($"{me.goldEarned:N0}".Replace(",", " "), nameFont, goldColor, new PointF(960, yOffset + 18));
    }
    
    /// <summary>
    /// Renders item icons in a row, maintaining an additional gap for the Trinket slot.
    /// </summary>
    private void DrawItems(IImageProcessingContext ctx, List<string> itemPaths, PointF startPos)
    {
        int iconSize = 40;
        int spacing = 2;
        int trinketGap = 10;

        for (int i = 0; i < itemPaths.Count; i++)
        {
            float currentX = startPos.X + (i * (iconSize + spacing));
        
            if (i == 6) currentX += trinketGap;

            if (string.IsNullOrEmpty(itemPaths[i]))
            {
                ctx.Fill(Color.FromRgb(20, 25, 30), new RectangleF(currentX, startPos.Y, iconSize, iconSize));
                continue;
            }

            try 
            {
                using var itemImg = Image.Load(itemPaths[i]);
                itemImg.Mutate(x => x.Resize(iconSize, iconSize));
                ctx.DrawImage(itemImg, new Point((int)currentX, (int)startPos.Y), 1f);
            }
            catch 
            {
                ctx.Fill(Color.FromRgb(40, 40, 40), new RectangleF(currentX, startPos.Y, iconSize, iconSize));
            }
        }
    }

    /// <summary>
    /// Releases resources used by the renderer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _renderQueue.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}