# ⚽ Football Discord Betting Bot

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Discord.Net](https://img.shields.io/badge/Discord.Net-3.19-5865F2?logo=discord&logoColor=white)](https://github.com/discord-net/Discord.Net)
[![Docker Ready](https://img.shields.io/badge/Docker-Enabled-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)
[![Tests](https://img.shields.io/badge/Tests-10%20Passed-brightgreen)](https://github.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A modern, production-ready Discord bot for organizing football betting contests on servers. Designed for major tournaments (FIFA World Cup, UEFA EURO) as well as domestic leagues (Premier League, Champions League, etc.).

Powered by **.NET 9**, **Discord.Net**, **Entity Framework Core (SQLite)**, and the **football-data.org** API.

---

## ✨ Features

- **⚽ Live Match Synchronization**: Automatically syncs fixtures, live status (`TIMED`, `IN_PLAY`, `FINISHED`), regular time scores, and extra-time / penalty shootouts from football-data.org.
- **🎯 Ephemeral & Fair Betting**: Place predictions using Discord Modals and Buttons. Tips remain secret until kickoff to prevent players from copying each other. At kickoff, all predictions are announced publicly.
- **🏆 Playoff & Overtime Scoring**:
  - **4 points**: Exact standard time (90 min) score.
  - **2 points**: Correct match winner with different score.
  - **2 points**: Predicted draw and match ended in a draw.
  - **+1 point**: Bonus for correctly predicting which team advances in playoff overtime/penalties.
  - **1 point**: Predicted regular-time team win, but that team won in overtime.
- **📊 Real-time Persistent Dashboards**: Dedicated Discord channels display an auto-updating Leaderboard and an 8-match Upcoming Fixtures overview with interactive action buttons.
- **🔔 Automated Kickoff Reminders**: Background worker alerts participants in advance if they haven't submitted their predictions yet.
- **🔄 Admin Recalculation**: `/recalc-match` command allows server admins to re-sync or adjust scores and recalculate points if an official result is corrected.
- **🐳 Docker & Cloud Ready**: Multi-stage Docker build and Docker Compose with volume-mounted SQLite persistence.

---

## 🛠️ Tech Stack

- **Runtime**: [.NET 9.0](https://dotnet.microsoft.com/) (C# 13)
- **Discord Library**: [Discord.Net 3.19](https://discordnet.dev/)
- **Database**: SQLite with Entity Framework Core 9.0
- **HTTP Client**: Typed `HttpClient` with JSON source mapping
- **Architecture**: Generic Host (`IHostedService`, `BackgroundService`), `IDbContextFactory`, Domain-driven `IScoringService`
- **Testing**: xUnit test suite for points calculation logic
- **Deployment**: Docker, Docker Compose

---

## 🚀 Quickstart

### Prerequisites

1. **Discord Bot Token**: Create an application on the [Discord Developer Portal](https://discord.com/developers/applications):
   - Under **Bot**, enable **Guilds** and **Server Members Intent** (Gateway Intents).
   - Generate your bot token and invite the bot to your server with `Send Messages`, `Embed Links`, and `Manage Messages` permissions.
2. **Football API Key**: Register a free API token at [football-data.org](https://www.football-data.org/).

---

### Option A: Running with Docker Compose (Recommended)

1. **Clone the repository**:
   ```bash
   git clone https://github.com/<your-username>/<your-repo-name>.git
   cd <your-repo-name>
   ```

2. **Configure environment variables**:
   ```bash
   cp .env.example .env
   ```
   Open `.env` in your editor and fill in your keys:
   ```env
   DiscordToken=your_discord_bot_token_here
   FootballApiKey=your_football_data_api_key_here
   NotificationChannelId=123456789012345678
   LeaderboardChannelId=123456789012345678
   ResultsChannelId=123456789012345678
   DailyMatchesChannelId=123456789012345678
   ```

3. **Start the container**:
   ```bash
   docker compose up -d --build
   ```

4. **View live logs**:
   ```bash
   docker compose logs -f
   ```

Data will be safely stored in the `./data/betting_bot.db` SQLite database on your host machine.

---

### Option B: Running Locally (.NET SDK)

1. **Clone the repository**:
   ```bash
   git clone https://github.com/<your-username>/<your-repo-name>.git
   cd <your-repo-name>
   ```

2. **Create configuration**:
   ```bash
   cp appsettings.example.json Discord/appsettings.json
   ```
   Edit `Discord/appsettings.json` with your credentials and channel IDs. *(Note: `appsettings.json` is git-ignored to prevent leaking credentials).*

3. **Build and Run**:
   ```bash
   dotnet build
   dotnet run --project Discord
   ```

---

## ⚙️ Configuration Reference

Configuration can be provided either through `appsettings.json` or standard environment variables (recommended for Docker):

| Setting | Environment Variable | Default | Description |
|---|---|---|---|
| `DiscordToken` | `DiscordToken` | *Required* | Discord Bot token from Developer Portal |
| `FootballApiKey` | `FootballApiKey` | *Required* | API key from football-data.org |
| `NotificationChannelId` | `NotificationChannelId` | *Required* | Discord channel ID for kickoff predictions and reminders |
| `LeaderboardChannelId` | `LeaderboardChannelId` | *Required* | Channel ID for the auto-updating persistent leaderboard |
| `ResultsChannelId` | `ResultsChannelId` | *Required* | Channel ID where finished match results and points are posted |
| `DailyMatchesChannelId` | `DailyMatchesChannelId` | *Required* | Channel ID for the persistent upcoming matches widget |
| `BotSettings:CompetitionId` | `BotSettings__CompetitionId` | `2000` | Competition ID (`2000` = World Cup, `2021` = Premier League, `2001` = Champions League, `2018` = EURO) |
| `BotSettings:SyncIntervalMinutes`| `BotSettings__SyncIntervalMinutes` | `10` | Frequency (in minutes) for fetching API updates |
| `BotSettings:DashboardDays` | `BotSettings__DashboardDays` | `12` | Window of days ahead to display in match dashboard |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | `Data Source=betting_bot.db` | SQLite database connection string |

---

## 🎮 Slash Commands

| Command | Permission | Description |
|---|---|---|
| `/matches` | Everyone | Opens the interactive match menu to place or update predictions |
| `/mybets` | Everyone | Displays your active predictions for upcoming matches |
| `/history [user]` | Everyone | Views your betting history (or another contestant's finished bets) |
| `/leaderboard` | Everyone | Displays the current top standings |
| `/help` | Everyone | Shows rules, command summaries, and scoring details |
| `/recalc-match <id>` | Administrator | Re-evaluates bets and recalculates points for a completed match |

---

## 🧪 Running Tests

Unit tests verify all point calculation scenarios, including regular time draws, playoff overtime advancement bonuses, and edge cases.

```bash
dotnet test
```

```text
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

---

## 🔒 Security & Privacy

- **No Secrets in Source**: Token inputs are git-ignored by default and loaded via environment variables or user secrets.
- **Fairness Guarantee**: Contestant predictions are stored with ephemeral Discord UI responses and remain hidden from other users until the exact second of kickoff.
- **Isolated SQLite Storage**: Persistent database files are kept in the mapped `/data` volume and ignored by version control.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
