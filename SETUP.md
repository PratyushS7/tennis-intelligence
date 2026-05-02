# Local Setup Guide

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 18](https://www.postgresql.org/download/) (or 15+)
- [Ollama](https://ollama.com/) (optional, for AI Coach)

## Database Setup

1. Install PostgreSQL with these defaults:
   - **Port**: 5432
   - **Username**: postgres
   - **Password**: postgres

2. No need to create the database manually — the app auto-creates and migrates on startup.

## Configuration

The real connection string lives in `appsettings.Development.json` (gitignored). Create it if missing:

```json
{
  "DetailedErrors": true,
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=tennis_intelligence;Username=postgres;Password=postgres"
  }
}
```

## Running the App

```bash
cd TennisIntelligence
dotnet run
```

The app starts at **http://localhost:5082**.

## AI Coach (Optional)

To enable the AI Coach with Ollama:

```bash
ollama pull llama3.2
ollama serve
```

The app auto-detects Ollama at `http://localhost:11434`. If unavailable, it falls back to rule-based coaching.

## Installing on Phone (PWA)

1. Make sure the app is accessible (locally or deployed to cloud)
2. Open the URL in your phone's browser
3. **Android Chrome**: tap ⋮ → "Add to Home Screen"
4. **iOS Safari**: tap Share → "Add to Home Screen"
