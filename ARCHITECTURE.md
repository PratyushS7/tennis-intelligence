# Architecture — Tennis Intelligence

## Design Philosophy

Tennis Intelligence follows a **goal-centric development loop**: the app helps you define what you're working on, track how it feels session by session, and get coaching that adapts to your focus areas.

The architecture is intentionally simple — ASP.NET Core Razor Pages with PostgreSQL. No SPA framework, no microservices, no over-engineering. It's a personal tool.

## Data Model

### Core Entities

```
DevelopmentGoal
├── Id, Name, Category, Description
├── Status (Active / Completed / Archived)
├── CreatedAt, CompletedAt
└── CheckIns[] ──→ GoalCheckIn

GoalCheckIn
├── Id, GoalId, SessionId
├── HowItFelt (Struggled / Okay / Clicked)
└── Note (optional)

Session
├── Id, Date, DurationMinutes, SessionType, MatchFormat
├── SessionRating (1-5 emoji scale)
├── BodyFeel (Good / Okay / Sore) — v2 simplified body check
├── EnergyLevel, EnergyBefore, EnergyAfter
├── ElbowPain?, ShoulderTightness? — nullable legacy fields
├── BreakdownAreas, BreakdownReasons — legacy comma-separated
├── FocusArea, FocusAchieved — legacy focus tracking
├── OpponentLevel, PlayStyle, MentalState, MatchResult — match context
├── Notes
└── GoalCheckIns[] ──→ GoalCheckIn

InteractionLog
├── Id, PageName, Action, Metadata, Timestamp
└── (indexes on Timestamp and Action+Timestamp)

ImportBatch
├── Source, FileName, SchemaVersion
├── ExportedAt, ImportedAt, Status
└── Inserted, Updated, Unchanged, Rejected counts

ExternalWorkout
├── Source + SourceRecordId — unique source identity
├── SourceApplication, ActivityType
├── StartedAt, EndedAt, SourceLastModifiedAt
├── Distance, calories, min/average/max heart rate
├── HeartRateSamples — detailed workout timeline
├── RawPayload — versioned source evidence
└── LastImportBatchId ──→ ImportBatch

ExternalDailySummary
├── Source + SummaryDate — unique daily identity
├── Steps, active/total calories, distance
├── Resting heart rate, HRV, oxygen saturation, VO2 max
├── Sleep duration and awake/light/deep/REM minutes
└── RawPayload + LastImportBatchId

ExternalBodyMeasurement
├── Source + SourceRecordId — unique source identity
├── MeasuredAt, WeightKg, BodyFatPercent
└── RawPayload + LastImportBatchId
```

### Key Relationships

- **Goal → CheckIns**: one-to-many, cascade delete
- **Session → GoalCheckIns**: one-to-many, cascade delete
- **GoalCheckIn**: unique constraint on (GoalId, SessionId) — one check-in per goal per session
- **Session deletion** cascades to remove associated goal check-ins (intentional: deleting evidence of play removes that data point)

### Design Decisions

**Why nullable pain fields?** The v2 pivot de-emphasizes body tracking. Making `ElbowPain` and `ShoulderTightness` nullable prevents new sessions (that skip the legacy body section) from producing fake "pain = 1" data that would mislead analytics and AI coaching.

**Why a soft cap of 5 active goals?** The session logging form shows check-ins for all active goals. More than 5 becomes unwieldy on mobile. Progressive disclosure (details hidden until "worked on it" is toggled) helps, but the cap keeps the form scannable.

**Why GoalCheckIn.SessionId is required (not nullable)?** A check-in represents work done on a goal during a specific session. Standalone reflections (outside sessions) are a different concept and would get a separate model if needed later.

**Why constrained string values instead of enums?** C# enums map to integers in PostgreSQL, which makes the database harder to read directly. String constants with static classes (e.g., `GoalCategories.Technique`, `GoalFeelings.Clicked`) keep the DB human-readable while preventing typo drift at compile time.

**Why keep wearable workouts separate from tennis sessions?** A tennis session is a subjective reflection, while an external workout is imported sensor evidence. Keeping them separate preserves provenance and allows a later linking workflow without making either record dependent on the other.

**Why retain the raw wearable payload?** Connector mappings will evolve as Samsung, Health Connect, and file formats change. Retaining the versioned source record allows corrected mappings and derived metrics to be rebuilt without re-exporting the original data.

**Why use Health Connect as the primary connector?** It provides one permission and schema layer across Samsung Health and other Android health sources. Schema version 2 preserves source identifiers, stores detailed workout heart-rate samples, and adds daily recovery/activity context. Source-aware upserts prevent repeated exports from duplicating records.

**Why is Samsung Health Data SDK optional?** Samsung-only values such as Energy Score and sleep score require Samsung's authenticated SDK download and developer mode for a personal debug build. The AAR is not stored in this repository; it must be downloaded under the user's Samsung Developer account and its SDK terms.

## Service Architecture

```
Pages (Razor Pages)
  ├── Index (Dashboard)
  ├── LogSession (Session + Goal check-in logging)
  ├── Goals (Goals Hub — create, complete, archive)
  ├── GoalDetail (Per-goal timeline and history)
  ├── History (Session list)
  ├── Insights (Legacy analytics — hidden from nav in v2)
  └── Coach (AI chat interface)

Services
  ├── CoachService — orchestrates AI coaching
  │   ├── BuildContextAsync() — builds SessionContext with goals + usage data
  │   ├── OllamaCoachProvider — LLM-based coaching (llama3.2)
  │   └── RuleBasedCoachProvider — pattern-based fallback
  └── InteractionService — tracks app usage for feedback loop

Filters
  └── InteractionLoggingFilter — global IAsyncPageFilter for automatic page view logging

Data
  └── TennisDbContext — EF Core context with all entities, managed via migrations
```

### AI Coach Flow

1. User asks a question on the Coach page
2. `CoachService.AskCoachAsync()` builds a `SessionContext` containing:
   - Session history and aggregate stats
   - Active development goals with check-in summaries
   - Recently completed goals
   - App usage patterns (interaction data)
3. The context is passed to the active provider (Ollama → RuleBased fallback)
4. Ollama builds a system prompt with all context sections; RuleBased uses pattern matching
5. Goals are the **primary recommendation source** when active goals exist

### Interaction Tracking

The interaction loop tracks **actionable signals**, not full analytics:

- **Automatic**: Page views via global filter (maps routes to page name constants)
- **Manual**: Semantic actions logged in page handlers (SessionLogged, GoalCreated, CoachAsked, etc.)
- **Consumption**: `InteractionService.GetUsageSummaryAsync()` computes ~10 usage signals
- **Delivery**: Usage data is included in the AI Coach's context for engagement nudges

Action and page name constants live in `Models/InteractionLog.cs` to prevent string drift.

## Migration Strategy

- EF Core migrations managed via `dotnet ef` (local tool, version 10.0.7)
- `Database.Migrate()` runs on startup — auto-applies pending migrations
- Fresh databases get the full schema from the migration chain
- Schema evolution: new columns are nullable or have safe defaults to preserve existing data

## File Organization

```
TennisIntelligence/
├── Data/           — DbContext
├── Filters/        — Global Razor Page filters
├── Migrations/     — EF Core migrations
├── Models/         — Entity models + string constants
├── Pages/          — Razor Pages (.cshtml + .cshtml.cs)
│   └── Shared/     — Layout template
├── Services/       — Business logic + AI coaching
├── wwwroot/        — Static assets (CSS, JS, Bootstrap)
├── Program.cs      — DI registration + middleware pipeline
└── appsettings.json — Configuration (DB connection, Ollama settings)
```
