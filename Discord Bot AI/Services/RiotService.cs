﻿using Discord_Bot_AI.Models;
using System.Net;
using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using Serilog;

namespace Discord_Bot_AI.Services;

/// <summary>
/// Service for interacting with the Riot Games API with built-in rate limiting and Polly retry policies.
/// </summary>
public class RiotService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://europe.api.riotgames.com/riot/account/v1/accounts";
    
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinRequestIntervalMs = 1200; 
    
    private DateTime _backoffUntil = DateTime.MinValue;
    private readonly object _backoffLock = new();
    
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private bool _disposed;

    /// <summary>
    /// Initializes the Riot service with API key and configures retry policies.
    /// </summary>
    /// <param name="apiKey">The Riot API key for authentication.</param>
    public RiotService(string apiKey)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", apiKey);
        
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
                        var retryAfter = response.Result.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, retryAttempt + 1));
                        return retryAfter;
                    }
                    return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                },
                onRetryAsync: async (outcome, timespan, retryAttempt, _) =>
                {
                    Log.Warning("Riot API retry {Attempt} after {Delay}s. Reason: {Reason}", 
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
    /// Retrieves Riot Account information based on in-game nickname and tag.
    /// </summary>
    /// <param name="gameNickName">The player's in-game name.</param>
    /// <param name="tag">The player's tag (e.g., EUNE, PL1).</param>
    /// <returns>The Riot account or null if not found.</returns>
    public async Task<RiotAccount?> GetAccountAsync(string gameNickName, string tag)
    {
        var url = $"{BaseUrl}/by-riot-id/{gameNickName}/{tag}";
        return await ExecuteWithRetryAsync<RiotAccount>(url);
    }

    /// <summary>
    /// Gets the latest match ID for a given player's PUUID.
    /// </summary>
    /// <param name="puuid">The player's unique identifier.</param>
    /// <returns>The latest match ID or null if not found.</returns>
    public async Task<string?> GetLatestMatchIdAsync(string puuid)
    {
        var url = $"https://europe.api.riotgames.com/lol/match/v5/matches/by-puuid/{puuid}/ids?start=0&count=1";
    
        var matchIds = await ExecuteWithRetryAsync<List<string>>(url);
        return matchIds?.FirstOrDefault();
    }
    
    /// <summary>
    /// Gets detailed match information based on match ID.
    /// </summary>
    /// <param name="matchId">The unique match identifier.</param>
    /// <returns>Detailed match data or null if not found.</returns>
    public async Task<MatchData?> GetMatchDetailsAsync(string matchId)
    {
        var url = $"https://europe.api.riotgames.com/lol/match/v5/matches/{matchId}";
        return await ExecuteWithRetryAsync<MatchData>(url);
    }

    /// <summary>
    /// Executes an HTTP request with rate limiting and Polly retry policy.
    /// </summary>
    /// <typeparam name="T">The expected response type.</typeparam>
    /// <param name="url">The request URL.</param>
    /// <returns>Deserialized response or default if failed.</returns>
    private async Task<T?> ExecuteWithRetryAsync<T>(string url) where T : class
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
            
            var response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetAsync(url));

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>();
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
                lock (_backoffLock)
                {
                    _backoffUntil = DateTime.UtcNow.Add(retryAfter);
                }
                Log.Warning("429 received from Riot API. Backing off for {Seconds}s", retryAfter.TotalSeconds);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            Log.Warning("Riot API request failed with status {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing Riot API request to {Url}", url);
            return null;
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

