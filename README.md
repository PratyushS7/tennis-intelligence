# 🎾 Tennis Intelligence

A personal tennis development companion. Define goals, track progress session by session, and get AI-powered coaching that adapts to your journey.

## Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL](https://www.postgresql.org/download/) running locally

### Setup

1. **Start PostgreSQL** on `localhost:5432`

2. **Update the connection string** (if your credentials differ) in `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Host=localhost;Port=5432;Database=tennis_intelligence;Username=postgres;Password=postgres"
   }
   ```

3. **Run the app**:
   ```bash
   cd TennisIntelligence
   dotnet run
   ```

4. **Open in browser**: `http://localhost:5082`

The database is created and migrated automatically on first run.

## Core Concept

Tennis Intelligence follows a **goal-centric development loop**:

1. **Define goals** — what aspects of your game you want to improve
2. **Log sessions** — record how each goal felt during play
3. **Track progress** — see your journey from struggling to clicking
4. **Get coaching** — AI adapts advice to your active goals and patterns

## Features

### 🎯 Goals Hub
- Create development goals with categories (Technique, Tactical, Mental, Fitness, Fundamentals)
- Track progress via emoji timeline (😤 Struggled → 😐 Okay → ✅ Clicked)
- Soft cap of 5 active goals to maintain focus
- Mark goals as completed or archive them
- Detailed view per goal with full check-in history

### 📝 Log Session
- Date, duration, session type (Practice/Match/Drill/Hitting), format (Singles/Doubles)
- Emoji session rating (😫 to 🔥)
- Body feel check (💪 Good / 👌 Okay / 🩹 Sore)
- **Goal check-ins** — toggle which goals you worked on, rate how they felt, add notes
- Energy before/after tracking
- Match context (opponent level, play style, mental state, result) — collapsible
- Legacy breakdown & body detail tracking — collapsible

### 📋 Session History
- All sessions with ratings, energy, focus results
- Desktop table + mobile card views
- Session deletion

### 🤖 AI Coach
- Multi-turn chat with Ollama LLM (llama3.2) or rule-based fallback
- Context-aware: sees your goals, session history, match patterns, and app usage
- Quick prompts for common questions
- Adapts advice to your active development goals

### 📊 Dashboard
- Session streak, total sessions, avg energy, active goal count
- Weekly session goal progress bar
- Session trends chart (energy, with optional body data overlay)
- Recent sessions table

### 📈 Interaction Tracking
- Automatic page view logging via global filter
- Semantic action tracking (sessions logged, goals created, coach questions)
- Usage data feeds into AI coaching for personalized engagement nudges

### ⌚ Wearable Imports
- Imports versioned JSON packages produced by phone and file connectors
- Preserves source identity and raw payloads
- Safely handles repeated imports through source-record upserts
- Reports inserted, updated, unchanged, and rejected records
- Supports workout heart-rate timelines, daily activity/recovery/sleep summaries, and body measurements
- Test packages are available at `samples/wearable-import-v1.json` and `samples/wearable-import-v2.json`

### Android connector API

The native companion app is in `../AndroidConnector`. It reads Samsung Health-compatible tennis workouts through Health Connect and sends the same package to:

```text
POST /api/connectors/workouts
X-Connector-Key: <configured key>
```

Configure the key through an environment variable rather than source control:

```powershell
$env:Connector__ApiKey = "generate-a-long-random-value"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5082"
dotnet run --no-launch-profile
```

Binding to `0.0.0.0` allows a physical phone on the same network to connect. The connector permits plain HTTP only in debug builds; release builds require HTTPS.

Open `AndroidConnector` in Android Studio, grant the requested Health Connect permissions, and enter this computer's LAN URL plus the configured connector key. See `AndroidConnector/README.md` for full setup details.

### Durable cloud database and backups

Production uses a Neon PostgreSQL connection supplied through `DATABASE_URL`; no database
credentials are committed. After authenticating the Neon CLI and selecting the project, run:

```powershell
.\scripts\Configure-NeonBackup.ps1
```

This stores the connection URL encrypted with Windows DPAPI for the current user, creates an
initial PostgreSQL custom-format backup, and registers a daily backup task. Backups are written
to `OneDrive\TennisTrackerBackups` when OneDrive is available, otherwise to
`Documents\TennisTrackerBackups`.

To pair the Android connector without copying the cloud connector key, run:

```powershell
.\scripts\Start-CloudPairing.ps1
```

Scan the local QR page with the phone camera. The QR targets the production HTTPS service while
the connector secret remains available only in the local process and Android encrypted storage.

## Tech Stack

- **Backend**: ASP.NET Core 10 (Razor Pages)
- **Database**: PostgreSQL with EF Core 10 (Npgsql), managed via migrations
- **AI**: Ollama (local LLM) with rule-based fallback coach
- **UI**: Bootstrap 5, mobile-first responsive design
- **No authentication** — single-user personal app

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed design decisions and data model documentation.
