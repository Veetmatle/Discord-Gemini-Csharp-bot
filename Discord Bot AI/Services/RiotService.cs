﻿using Discord_Bot_AI.Models;
using Discord_Bot_AI.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using Serilog;

namespace Discord_Bot_AI.Services;

/// <summary>
/// Service for interacting with the Riot Games API with built-in rate limiting.
/// Uses IHttpClientFactory for proper HTTP client lifecycle management.
/// Retry policies are configured centrally in ServiceCollectionExtensions.
/// </summary>
public class RiotService : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string BaseUrl = "https://europe.api.riotgames.com/riot/account/v1/accounts";
    
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinRequestIntervalMs = 1200; 
    
    private DateTime _backoffUntil = DateTime.MinValue;
    private readonly object _backoffLock = new();
    
    private bool _disposed;

    /// <summary>
    /// Initializes the Riot service with IHttpClientFactory for managed HTTP connections.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
    public RiotService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
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
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The Riot account or null if not found.</returns>
    public async Task<RiotAccount?> GetAccountAsync(string gameNickName, string tag,
        CancellationToken cancellationToken = default)
    {
        var encodedName = Uri.EscapeDataString(gameNickName);
        var encodedTag = Uri.EscapeDataString(tag);
        var url = $"{BaseUrl}/by-riot-id/{encodedName}/{encodedTag}";
        var account = await ExecuteWithRateLimitingAsync<RiotAccount>(url, cancellationToken);
        Log.Information("Account lookup for {GameNickName}#{Tag} returned: {Account}", gameNickName, tag, account != null ? account.puuid : "null");
        
        return account;
    }

    /// <summary>
    /// Gets the latest match ID for a given player's PUUID.
    /// </summary>
    /// <param name="puuid">The player's unique identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The latest match ID or null if not found.</returns>
    public async Task<string?> GetLatestMatchIdAsync(string puuid, CancellationToken cancellationToken = default)
    {
        var encodedPuuid = Uri.EscapeDataString(puuid);
        var url = $"https://europe.api.riotgames.com/lol/match/v5/matches/by-puuid/{encodedPuuid}/ids?start=0&count=1";
    
        var matchIds = await ExecuteWithRateLimitingAsync<List<string>>(url, cancellationToken);
        return matchIds?.FirstOrDefault();
    }
    
    /// <summary>
    /// Gets detailed match information based on match ID.
    /// </summary>
    public async Task<MatchData?> GetMatchDetailsAsync(string matchId, CancellationToken cancellationToken = default)
    {
        var encodedMatchId = Uri.EscapeDataString(matchId);
        var url = $"https://europe.api.riotgames.com/lol/match/v5/matches/{encodedMatchId}";
        return await ExecuteWithRateLimitingAsync<MatchData>(url, cancellationToken);
    }

    /// <summary>
    /// Gets the latest TFT match ID for a given player's PUUID.
    /// </summary>
    public async Task<string?> GetLatestTftMatchIdAsync(string puuid, CancellationToken cancellationToken = default)
    {
        var encodedPuuid = Uri.EscapeDataString(puuid);
        var url = $"https://europe.api.riotgames.com/tft/match/v1/matches/by-puuid/{encodedPuuid}/ids?start=0&count=1";
        var matchIds = await ExecuteWithRateLimitingAsync<List<string>>(url, cancellationToken);
        return matchIds?.FirstOrDefault();
    }

    /// <summary>
    /// Gets detailed TFT match information based on match ID.
    /// </summary>
    public async Task<TftMatchData?> GetTftMatchDetailsAsync(string matchId, CancellationToken cancellationToken = default)
    {
        var encodedMatchId = Uri.EscapeDataString(matchId);
        var url = $"https://europe.api.riotgames.com/tft/match/v1/matches/{encodedMatchId}";
        return await ExecuteWithRateLimitingAsync<TftMatchData>(url, cancellationToken);
    }

    /// <summary>
    /// Executes an HTTP request with rate limiting. Retry logic is handled by Polly policies.
    /// </summary>
    /// <typeparam name="T">The expected response type.</typeparam>
    /// <param name="url">The request URL.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Deserialized response or default if failed.</returns>
    private async Task<T?> ExecuteWithRateLimitingAsync<T>(string url, CancellationToken cancellationToken) where T : class
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
            
            var httpClient = _httpClientFactory.CreateClient(HttpClientNames.RiotApi);
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
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
            
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Log.Error("Riot API returned Unauthorized (401). Check if RIOT_TOKEN is valid and not expired");
                return null;
            }
            
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Log.Error("Riot API returned Forbidden (403). The API key may lack required permissions");
                return null;
            }
            
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Log.Error("Riot API returned BadRequest (400). URL: {Url}, Response: {Error}", url, errorContent);
                return null;
            }

            Log.Warning("Riot API request failed with status {StatusCode}", response.StatusCode);
            return null;
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Riot API request cancelled");
            throw;
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

