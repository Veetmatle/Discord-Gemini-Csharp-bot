﻿using System.Net;
using System.Text;
using Discord_Bot_AI.Models.Gemini;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using Serilog;

namespace Discord_Bot_AI.Services;

/// <summary>
/// Service for interacting with Google's Gemini AI API with rate limiting and Polly retry policies.
/// </summary>
public class GeminiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
    private readonly string _promptPrefix =
        "\n Answer in Polish in max 100 words. Be brief and precise unless instructions say otherwise.";
    
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinRequestIntervalMs = 1000;
    
    private DateTime _backoffUntil = DateTime.MinValue;
    private readonly object _backoffLock = new();
    
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private bool _disposed;

    /// <summary>
    /// Initializes the Gemini service with API key and configures retry policies.
    /// </summary>
    /// <param name="apiKey">The Google Gemini API key for authentication.</param>
    public GeminiService(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (retryAttempt, response, _) =>
                {
                    if (response.Result?.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt + 1));
                    }
                    return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                },
                onRetryAsync: async (outcome, timespan, retryAttempt, _) =>
                {
                    Log.Warning("Gemini API retry {Attempt} after {Delay}s. Reason: {Reason}", 
                        retryAttempt, timespan.TotalSeconds, outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message);
                    await Task.CompletedTask;
                });
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
    /// <returns>The AI-generated response or an error message.</returns>
    public async Task<string> GetAnswerAsync(string question)
    {
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
                await Task.Delay(waitTime);
            }
        }

        await _rateLimiter.WaitAsync();
        try
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            if (timeSinceLastRequest.TotalMilliseconds < MinRequestIntervalMs)
            {
                var delay = MinRequestIntervalMs - (int)timeSinceLastRequest.TotalMilliseconds;
                await Task.Delay(delay);
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
            
            var response = await _retryPolicy.ExecuteAsync(() => 
                _httpClient.PostAsync($"{ApiUrl}?key={_apiKey}", content));
            
            var responseJson = await response.Content.ReadAsStringAsync();

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
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _rateLimiter.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

