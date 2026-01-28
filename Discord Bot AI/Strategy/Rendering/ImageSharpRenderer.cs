using Discord_Bot_AI.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace Discord_Bot_AI.Strategy.Rendering;

public class ImageSharpRenderer : IGameSummaryRenderer
{
    private readonly FontCollection _fontCollection = new();
    private readonly FontFamily _headingFamily;
    private readonly FontFamily _statsFamily;
    
    public ImageSharpRenderer()
    {
        _headingFamily = _fontCollection.Add("Assets/Fonts/Cinzel static/Cinzel-Bold.ttf");
        _statsFamily = _fontCollection.Add("Assets/Fonts/Roboto static/RobotoCondensed-Bold.ttf");
    }
    public async Task<Stream> RenderSummaryAsync(RiotAccount account, MatchData matchData)
    {
        var me = matchData.info.participants.FirstOrDefault(p => p.puuid == account.puuid);
        if (me == null) throw new Exception("Player not found in match data");
        
        using Image<Rgba32> image = new Image<Rgba32>(1000, 300);
        
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgb(10, 20, 25));
            
            var titleFont = _headingFamily.CreateFont(45);
            string titleText = me.win ? "ZWYCIĘSTWO" : "PORAŻKA";
            Color titleColor = me.win ? Color.Gold : Color.Crimson;
            
            ctx.DrawText(titleText, titleFont, titleColor, new PointF(50, 40));
        });
        
        var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        ms.Position = 0;
        return ms;
    }
}