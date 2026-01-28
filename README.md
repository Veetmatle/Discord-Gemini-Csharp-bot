# Discord Bot AI

A Discord bot with League of Legends match tracking and Gemini AI integration.

## Requirements

- Docker & Docker Compose
- Discord Bot Token
- Google Gemini API Key
- Riot Games API Key

## Quick Start

### 1. Clone and configure

```bash
# Copy environment template
cp .env.example .env

# Edit .env with your API keys
nano .env
```

### 2. Start the bot

```bash
docker compose up -d
```

### 3. Check logs

```bash
docker compose logs -f
```

### 4. Stop the bot

```bash
docker compose down
```

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `DISCORD_TOKEN` | ✅ | Discord bot token |
| `GEMINI_API_KEY` | ✅ | Google Gemini API key |
| `RIOT_TOKEN` | ✅ | Riot Games API key |
| `SERVER_IDS` | ✅ | Comma-separated Discord server IDs |
| `RIOT_VERSION` | ❌ | Data Dragon version (default: 14.2.1) |
| `DATA_PATH` | ❌ | User data directory (default: /app/data) |
| `CACHE_PATH` | ❌ | Image cache directory (default: /app/cache) |
| `LOG_PATH` | ❌ | Log files directory (default: /app/logs) |

## Data Persistence

The following Docker volumes are used for data persistence:

- `discord-bot-data` - User registrations (survives container restarts)
- `discord-bot-cache` - Downloaded game assets
- `discord-bot-logs` - Application logs

## Development

### Run locally (without Docker)

```bash
# Set environment variables
export DISCORD_TOKEN=your_token
export GEMINI_API_KEY=your_key
export RIOT_TOKEN=your_key
export SERVER_IDS=123456789

# Run
cd "Discord Bot AI"
dotnet run
```

### Build Docker image manually

```bash
docker build -t discord-bot-ai .
```

## Security Notes

- ⚠️ **NEVER commit `.env` to version control**
- The container runs as a non-root user
- Read-only filesystem with tmpfs for temporary files
- Resource limits prevent runaway processes

## Architecture

```
Discord Bot AI/
├── Configuration/     # Settings and providers (SOLID)
├── Data/              # User registry with persistence
├── Models/            # Data models
├── Services/          # Core services (Riot, Gemini, etc.)
└── Strategy/          # Notification and rendering strategies
```
