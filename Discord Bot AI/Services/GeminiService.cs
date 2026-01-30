using System.Net;
using System.Text;
using Discord_Bot_AI.Configuration;
using Discord_Bot_AI.Infrastructure;
using Discord_Bot_AI.Models.Gemini;
using Newtonsoft.Json;
using Serilog;

namespace Discord_Bot_AI.Services;

/// <summary>
/// Service for interacting with Google's Gemini AI API with rate limiting.
/// Uses IHttpClientFactory for proper HTTP client lifecycle management.
/// Retry policies are configured centrally in ServiceCollectionExtensions.
/// </summary>
public class GeminiService : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
    private readonly string _promptPrefix =
        "\n Answer in Polish in max 100 words. Be brief and precise unless instructions say otherwise.";
    
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinRequestIntervalMs = 1000;
    
    private DateTime _backoffUntil = DateTime.MinValue;
    private readonly object _backoffLock = new();
    
    private bool _disposed;

    /// <summary>
    /// Initializes the Gemini service with IHttpClientFactory for managed HTTP connections.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
    /// <param name="settings">Application settings containing API key.</param>
    public GeminiService(IHttpClientFactory httpClientFactory, AppSettings settings)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = settings.GeminiApiKey;
    }

    /// <summary>
    /// Checks if the service is currently in a rate-limit backoff state.
    /// </summary>
    public bool IsRateLimited
    {
        get
        {
            lock (_backoffLock)
            {
                return DateTime.UtcNow < _backoffUntil;
            }
        }
    }

    /// <summary>
    /// Sends a prompt to Google's Gemini API and retrieves the generated text response.
    /// </summary>
    /// <param name="question">The question or prompt to send to the AI.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The AI-generated response or an error message.</returns>
    public async Task<string> GetAnswerAsync(string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (IsRateLimited)
        {
            TimeSpan waitTime;
            lock (_backoffLock)
            {
                waitTime = _backoffUntil - DateTime.UtcNow;
            }
            if (waitTime > TimeSpan.Zero)
            {
                Log.Debug("Rate limited. Waiting {WaitTime}s before retry", waitTime.TotalSeconds);
                await Task.Delay(waitTime, cancellationToken);
            }
        }

        await _rateLimiter.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            if (timeSinceLastRequest.TotalMilliseconds < MinRequestIntervalMs)
            {
                var delay = MinRequestIntervalMs - (int)timeSinceLastRequest.TotalMilliseconds;
                await Task.Delay(delay, cancellationToken);
            }
            
            _lastRequestTime = DateTime.UtcNow;

            var requestBody = new GeminiRequest
            {
                contents = new[]
                {
                    new Content { parts = new[] { new Part { text = question + _promptPrefix } } }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var httpClient = _httpClientFactory.CreateClient(HttpClientNames.GeminiApi);
            var response = await httpClient.PostAsync($"{ApiUrl}?key={_apiKey}", content, cancellationToken);
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(responseJson);
                string? answer = result?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                if (string.IsNullOrEmpty(answer))
                    return "No answer found.";
                
                return answer;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = TimeSpan.FromSeconds(60);
                lock (_backoffLock)
                {
                    _backoffUntil = DateTime.UtcNow.Add(retryAfter);
                }
                Log.Warning("429 received from Gemini API. Backing off for {Seconds}s", retryAfter.TotalSeconds);
            }

            Log.Warning("Gemini API request failed with status {StatusCode}", response.StatusCode);
            return $"Error: {response.StatusCode}";
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Gemini API request cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing Gemini API request");
            return $"Error: {ex.Message}";
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    /// <summary>
    /// Releases resources used by the service.
    /// Note: HttpClient is managed by IHttpClientFactory, no need to dispose it manually.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _rateLimiter.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

