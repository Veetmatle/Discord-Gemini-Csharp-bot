﻿using Newtonsoft.Json;
using Discord_Bot_AI.Models;
using Serilog;

namespace Discord_Bot_AI.Data;

/// <summary>
/// Thread-safe registry for managing Discord user to Riot account mappings.
/// Uses ReaderWriterLockSlim for efficient concurrent read access with exclusive write access.
/// </summary>
public class UserRegistry : IUserRegistry
{
    private readonly string _filePath;
    private Dictionary<ulong, RiotAccount> _userMap = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private bool _disposed;

    /// <summary>
    /// Creates a new UserRegistry with configurable data path for Docker volume support.
    /// </summary>
    /// <param name="dataPath">The directory path where user data will be stored.</param>
    public UserRegistry(string dataPath = ".")
    {
        Directory.CreateDirectory(dataPath);
        _filePath = Path.Combine(dataPath, "users.json");
        Load();
        Log.Information("UserRegistry initialized with data path: {DataPath}", dataPath);
    }

    /// <summary>
    /// Registers or updates a Riot account for a Discord user.
    /// </summary>
    public void RegisterUser(ulong discordUserId, RiotAccount account)
    {
        _lock.EnterWriteLock();
        try
        {
            _userMap[discordUserId] = account;
            SaveInternal();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Updates the last match ID for an existing account without full re-registration.
    /// </summary>
    /// <param name="discordUserId">The Discord user's unique identifier.</param>
    /// <param name="lastMatchId">The new last match ID to store.</param>
    public void UpdateLastMatchId(ulong discordUserId, string lastMatchId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_userMap.TryGetValue(discordUserId, out var account))
            {
                account.LastMatchId = lastMatchId;
                SaveInternal();
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Removes a user's Riot account registration.
    /// </summary>
    /// <param name="discordUserId">The Discord user's unique identifier.</param>
    /// <returns>True if the account was found and removed, false otherwise.</returns>
    public bool RemoveUser(ulong discordUserId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_userMap.Remove(discordUserId))
            {
                SaveInternal(); 
                return true;
            }
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Retrieves a Riot account associated with a Discord user.
    /// </summary>
    /// <param name="discordUserId">The Discord user's unique identifier.</param>
    /// <returns>The associated Riot account or null if not found.</returns>
    public RiotAccount? GetAccount(ulong discordUserId)
    {
        _lock.EnterReadLock();
        try
        {
            return _userMap.GetValueOrDefault(discordUserId);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Returns a snapshot of all tracked users. Safe for iteration while other operations continue.
    /// </summary>
    /// <returns>A copy of all user-account pairs.</returns>
    public List<KeyValuePair<ulong, RiotAccount>> GetAllTrackedUsers()
    {
        _lock.EnterReadLock();
        try
        {
            return _userMap.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Internal save method - must be called within a write lock.
    /// </summary>
    private void SaveInternal()
    {
        string json = JsonConvert.SerializeObject(_userMap, Formatting.Indented);
        
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// Loads the user registry from a JSON file if it exists.
    /// </summary>
    private void Load()
    {
        if (File.Exists(_filePath))
        {
            string json = File.ReadAllText(_filePath);
            _userMap = JsonConvert.DeserializeObject<Dictionary<ulong, RiotAccount>>(json) ?? new();
            Log.Information("Loaded {Count} users from registry", _userMap.Count);
        }
    }

    /// <summary>
    /// Releases resources used by the registry.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _lock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}