using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discord_Bot_AI.Infrastructure;
using HtmlAgilityPack;
using Serilog;

namespace Discord_Bot_AI.Services;

/// <summary>
/// Service for scraping Politechnika Krakowska WIiT website and finding relevant links
/// </summary>
public class PolitechnikaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiService _geminiService;
    
    private const string BaseUrl = "https://it.pk.edu.pl";
    private const string StudentPageUrl = "https://it.pk.edu.pl/studenci/";
    private const long MaxDiscordFileSize = 25 * 1024 * 1024; // 25MB Discord limit
    
    private readonly SemaphoreSlim _scrapeLock = new(1, 1);
    private Dictionary<string, string> _cachedLinks = new();
    private DateTime _lastScrapeTime = DateTime.MinValue;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);

    /// <summary>
    /// Initializes the Politechnika service with HTTP client and Gemini AI.
    /// </summary>
    public PolitechnikaService(IHttpClientFactory httpClientFactory, GeminiService geminiService)
    {
        _httpClientFactory = httpClientFactory;
        _geminiService = geminiService;
    }

    /// <summary>
    /// Processes a user query about Politechnika Krakowska WIiT and returns
    /// either a direct link, file information, or a text response with source.
    /// </summary>
    public async Task<PolitechnikaResponse> ProcessQueryAsync(string userQuery, CancellationToken cancellationToken = default)
    {
        try
        {
            var links = await GetScrapedLinksAsync(false, cancellationToken);
            
            if (links.Count == 0)
            {
                return new PolitechnikaResponse
                {
                    Success = false,
                    Message = "Nie udało się pobrać danych z PK. Spróbuj ponownie później.",
                    SourceUrl = StudentPageUrl
                };
            }
            
            var prompt = BuildGeminiPrompt(userQuery, links);
            
            var geminiResponse = await _geminiService.GetAnswerAsync(
                new Models.GeminiRequest { Prompt = prompt }, 
                cancellationToken);
            
            return ParseGeminiResponse(geminiResponse, userQuery, links);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing Politechnika query: {Query}", userQuery);
            return new PolitechnikaResponse
            {
                Success = false,
                Message = $"Wystąpił błąd podczas przetwarzania zapytania: {ex.Message}",
                SourceUrl = StudentPageUrl
            };
        }
    }

    /// <summary>
    /// Scrapes links from Politechnika WIiT website with caching.
    /// </summary>
    public async Task<Dictionary<string, string>> GetScrapedLinksAsync(
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && DateTime.UtcNow - _lastScrapeTime < CacheExpiration && _cachedLinks.Count > 0)
        {
            Log.Debug("Using cached Politechnika links ({Count} links)", _cachedLinks.Count);
            return _cachedLinks;
        }

        await _scrapeLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && DateTime.UtcNow - _lastScrapeTime < CacheExpiration && _cachedLinks.Count > 0)
            {
                return _cachedLinks;
            }

            Log.Information("Scraping Politechnika WIiT website{ForceRefresh}...", 
                forceRefresh ? " (forced refresh)" : "");
            
            var allLinks = new Dictionary<string, string>();
            
            var pagesToScrape = new[]
            {
                "https://it.pk.edu.pl/studenci/",
                "https://it.pk.edu.pl/studenci/studia-i-stopnia/",
                "https://it.pk.edu.pl/studenci/studia-ii-stopnia/",
                "https://it.pk.edu.pl/studenci/harmonogramy/",
                "https://it.pk.edu.pl/studenci/plany-zajec/",
                "https://it.pk.edu.pl/studenci/praktyki-i-staze/",
                "https://it.pk.edu.pl/studenci/regulaminy-i-dokumenty/",
                "https://it.pk.edu.pl/studenci/stypendia/",
                "https://it.pk.edu.pl/studenci/prace-dyplomowe/",
                "https://it.pk.edu.pl/studenci/na-studiach/",
                "https://it.pk.edu.pl/studenci/na-studiach/rozklady-zajec/",
                "https://it.pk.edu.pl/studenci/na-studiach/dyplomy-egzamin-dyplomowy/",
                "https://it.pk.edu.pl/studenci/na-studiach/harmonogram-sesji-egzaminacyjnej/",
                "https://it.pk.edu.pl/studenci/na-studiach/organizacja-roku-akademickiego-2025-2026-r/",
                "https://it.pk.edu.pl/studenci/na-studiach/praktyki/",
                "https://it.pk.edu.pl/studenci/na-studiach/zasady-kwalifikacji-studentow-na-przedmioty-wybieralne/",
                "https://it.pk.edu.pl/studenci/na-studiach/konkursy-na-prace-dyplomowe/"
            };

            using var httpClient = _httpClientFactory.CreateClient(HttpClientNames.GeminiApi);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            foreach (var pageUrl in pagesToScrape)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var pageLinks = await ScrapePageAsync(httpClient, pageUrl, cancellationToken);
                    foreach (var link in pageLinks)
                    {
                        allLinks.TryAdd(link.Key, link.Value);
                    }
                    
                    await Task.Delay(200, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to scrape page: {Url}", pageUrl);
                }
            }

            _cachedLinks = allLinks;
            _lastScrapeTime = DateTime.UtcNow;
            
            Log.Information("Scraped {Count} links from Politechnika website", allLinks.Count);
            return allLinks;
        }
        finally
        {
            _scrapeLock.Release();
        }
    }

    /// <summary>
    /// Scrapes a single page for links.
    /// </summary>
    private async Task<Dictionary<string, string>> ScrapePageAsync(
        HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        var links = new Dictionary<string, string>();
        
        var html = await httpClient.GetStringAsync(url, cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        
        var anchorNodes = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchorNodes == null) return links;

        foreach (var anchor in anchorNodes)
        {
            var href = anchor.GetAttributeValue("href", "");
            var text = CleanText(anchor.InnerText);
            
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                continue;
            
            var fullUrl = NormalizeUrl(href);
            if (string.IsNullOrEmpty(fullUrl))
                continue;
            
            if (IsRelevantLink(fullUrl, text))
            {
                links.TryAdd(text, fullUrl);
            }
        }

        return links;
    }

    /// <summary>
    /// Normalizes a URL to absolute form.
    /// </summary>
    private static string NormalizeUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "";
        
        if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("#"))
            return "";
        
        if (href.StartsWith("http://") || href.StartsWith("https://"))
            return href;
        
        if (href.StartsWith("/"))
            return BaseUrl + href;

        return BaseUrl + "/" + href;
    }

    /// <summary>
    /// Checks if a link is relevant for student queries.
    /// </summary>
    private static bool IsRelevantLink(string url, string text)
    {
        var documentExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" };
        if (documentExtensions.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return true;
        
        if (url.Contains("pk.edu.pl", StringComparison.OrdinalIgnoreCase))
            return true;
        
        var keywords = new[] { "plan", "harmonogram", "regulamin", "sylabus", "rozklad", "lista", 
            "praktyk", "stypendi", "dyplom", "egzamin", "sesja", "rekrutacja" };
        var lowerText = text.ToLowerInvariant();
        var lowerUrl = url.ToLowerInvariant();
        
        return keywords.Any(k => lowerText.Contains(k) || lowerUrl.Contains(k));
    }

    /// <summary>
    /// Cleans text from HTML artifacts.
    /// </summary>
    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    /// <summary>
    /// Builds the Gemini prompt for finding the best matching link.
    /// Uses JSON response format for reliable parsing.
    /// </summary>
    private static string BuildGeminiPrompt(string userQuery, Dictionary<string, string> links)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Jesteś inteligentnym asystentem studenta Wydziału Informatyki i Telekomunikacji Politechniki Krakowskiej (WIiT PK).");
        sb.AppendLine();
        sb.AppendLine("Zadanie: Na podstawie dostarczonej listy linków (tekst + URL) wyciągniętych ze strony it.pk.edu.pl, musisz wskazać JEDEN adres URL, który najlepiej odpowiada na zapytanie użytkownika.");
        sb.AppendLine();
        sb.AppendLine("Kryteria wyboru:");
        sb.AppendLine("1. Aktualność: Jeśli na liście jest kilka podobnych plików (np. plany zajęć), zawsze wybieraj ten z najnowszą datą w nazwie lub opisie.");
        sb.AppendLine("2. Precyzja: Jeśli użytkownik pyta o 'Informatykę I stopnia', ignoruj wyniki dla 'II stopnia'.");
        sb.AppendLine("3. Typ zawartości: Pierwszeństwo mają bezpośrednie pliki (.pdf, .xlsx, .xls), jeśli zapytanie sugeruje szukanie konkretnego dokumentu.");
        sb.AppendLine();
        sb.AppendLine("WAŻNE - Format odpowiedzi:");
        sb.AppendLine("Odpowiedz TYLKO w formacie JSON (bez markdown, bez ```json):");
        sb.AppendLine("- Jeśli znaleziono: {\"url\": \"https://...\"}");
        sb.AppendLine("- Jeśli nie znaleziono: {\"url\": \"NOT_FOUND\"}");
        sb.AppendLine();
        sb.AppendLine($"Zapytanie użytkownika: {userQuery}");
        sb.AppendLine();
        sb.AppendLine("Lista linków:");
        
        foreach (var link in links)
        {
            sb.AppendLine($"- {link.Key}: {link.Value}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses the Gemini response (JSON format) and builds the final response.
    /// Validates that the returned URL exists in the scraped links list.
    /// </summary>
    private static PolitechnikaResponse ParseGeminiResponse(
        string geminiResponse, string userQuery, Dictionary<string, string> links)
    {
        var response = geminiResponse.Trim();
        
        string? foundUrl = null;
        
        try
        {
            response = response
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();
            
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("url", out var urlElement))
            {
                foundUrl = urlElement.GetString();
            }
        }
        catch (JsonException)
        {
            Log.Warning("Failed to parse Gemini JSON response, falling back to regex");
            var urlMatch = Regex.Match(response, @"https?://[^\s<>""'}\]]+");
            if (urlMatch.Success)
            {
                foundUrl = urlMatch.Value.TrimEnd('.', ',', ')', ']', '}');
            }
        }
        
        if (string.IsNullOrEmpty(foundUrl) || 
            foundUrl.Equals("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return new PolitechnikaResponse
            {
                Success = false,
                Message = $"Nie znaleziono pasującego linku dla zapytania: \"{userQuery}\". " +
                         "Spróbuj przeformułować pytanie lub sprawdź stronę bezpośrednio.",
                SourceUrl = StudentPageUrl
            };
        }
        
        var matchingLink = links.FirstOrDefault(l => 
            l.Value.Equals(foundUrl, StringComparison.OrdinalIgnoreCase));
        
        if (matchingLink.Key == null)
        {
            Log.Warning("Gemini returned URL not in scraped list: {Url}", foundUrl);
            
            matchingLink = links.FirstOrDefault(l => 
                foundUrl.Contains(l.Value, StringComparison.OrdinalIgnoreCase) ||
                l.Value.Contains(foundUrl, StringComparison.OrdinalIgnoreCase));
            
            if (matchingLink.Key == null)
            {
                return new PolitechnikaResponse
                {
                    Success = false,
                    Message = "Znaleziony link nie został zweryfikowany. Spróbuj ponownie lub sprawdź stronę bezpośrednio.",
                    SourceUrl = StudentPageUrl
                };
            }
            
            foundUrl = matchingLink.Value;
        }
        
        var linkText = matchingLink.Key ?? "Znaleziony link";
        var isFile = IsFileUrl(foundUrl);
        
        return new PolitechnikaResponse
        {
            Success = true,
            Url = foundUrl,
            LinkText = linkText,
            IsFile = isFile,
            FileType = isFile ? GetFileType(foundUrl) : null,
            Message = isFile 
                ? $"Znaleziono plik: **{linkText}**"
                : $"Znaleziono: **{linkText}**",
            SourceUrl = StudentPageUrl
        };
    }

    /// <summary>
    /// Checks if URL points to a downloadable file.
    /// </summary>
    private static bool IsFileUrl(string url)
    {
        var fileExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".rar" };
        return fileExtensions.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the file type description from URL.
    /// </summary>
    private static string GetFileType(string url)
    {
        var extension = Path.GetExtension(url).ToUpperInvariant().TrimStart('.');
        return extension switch
        {
            "PDF" => "PDF",
            "DOC" or "DOCX" => "Word",
            "XLS" or "XLSX" => "Excel",
            "PPT" or "PPTX" => "PowerPoint",
            "ZIP" or "RAR" => "Archiwum",
            _ => extension
        };
    }
    
    /// <summary>
    /// Checks if a file can be downloaded and uploaded to Discord (under 25MB).
    /// Uses HEAD request to check file size without downloading.
    /// </summary>
    public async Task<FileDownloadInfo> CheckFileDownloadableAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(HttpClientNames.GeminiApi);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            using var request = new HttpRequestMessage(HttpMethod.Head, fileUrl);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return new FileDownloadInfo
                {
                    CanDownload = false,
                    Reason = $"Nie można pobrać pliku (HTTP {(int)response.StatusCode})"
                };
            }
            
            var contentLength = response.Content.Headers.ContentLength;
            
            if (contentLength == null)
            {
                return new FileDownloadInfo
                {
                    CanDownload = true,
                    FileSize = null,
                    FileName = GetFileNameFromUrl(fileUrl)
                };
            }
            
            if (contentLength > MaxDiscordFileSize)
            {
                return new FileDownloadInfo
                {
                    CanDownload = false,
                    FileSize = contentLength.Value,
                    Reason = $"Plik jest za duży ({contentLength.Value / 1024 / 1024:F1} MB). Limit Discord to 25 MB."
                };
            }
            
            return new FileDownloadInfo
            {
                CanDownload = true,
                FileSize = contentLength.Value,
                FileName = GetFileNameFromUrl(fileUrl)
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check file downloadability: {Url}", fileUrl);
            return new FileDownloadInfo
            {
                CanDownload = false,
                Reason = "Nie można sprawdzić rozmiaru pliku"
            };
        }
    }
    
    /// <summary>
    /// Downloads a file from URL and returns it as a stream for Discord upload.
    /// </summary>
    public async Task<(MemoryStream? Stream, string FileName)?> DownloadFileAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var downloadInfo = await CheckFileDownloadableAsync(fileUrl, cancellationToken);
            if (!downloadInfo.CanDownload)
            {
                Log.Warning("File not downloadable: {Reason}", downloadInfo.Reason);
                return null;
            }
            
            using var httpClient = _httpClientFactory.CreateClient(HttpClientNames.GeminiApi);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            var bytes = await httpClient.GetByteArrayAsync(fileUrl, cancellationToken);
            
            if (bytes.Length > MaxDiscordFileSize)
            {
                Log.Warning("Downloaded file exceeds Discord limit: {Size} bytes", bytes.Length);
                return null;
            }
            
            var stream = new MemoryStream(bytes);
            var fileName = downloadInfo.FileName ?? GetFileNameFromUrl(fileUrl);
            
            Log.Information("Downloaded file: {FileName} ({Size} bytes)", fileName, bytes.Length);
            return (stream, fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download file: {Url}", fileUrl);
            return null;
        }
    }
    
    /// <summary>
    /// Extracts filename from URL.
    /// </summary>
    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrEmpty(fileName) ? "document" : fileName;
        }
        catch
        {
            return "document";
        }
    }
}

/// <summary>
/// Information about file download capability.
/// </summary>
public class FileDownloadInfo
{
    public bool CanDownload { get; set; }
    public long? FileSize { get; set; }
    public string? FileName { get; set; }
    public string? Reason { get; set; }
}

public class PolitechnikaResponse
{
    public bool Success { get; set; }
    public string? Url { get; set; }
    public string? LinkText { get; set; }
    public bool IsFile { get; set; }
    public string? FileType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
}
