using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Discord.Data;
using Discord.Models;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Discord.Services
{
    public class MatchSyncWorker : BackgroundService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly ILogger<MatchSyncWorker> _logger;
        private readonly FootballApiService _apiService;
        private readonly DiscordSocketClient _discordClient;
        private readonly IConfiguration _configuration;
        private readonly IScoringService _scoringService;
        
        private DateTime? _last15NotificationDate;
        private DateTime? _last22NotificationDate;
        
        private int CompetitionId => _configuration.GetValue<int>("BotSettings:CompetitionId", 2000);
        private TimeSpan SyncInterval => TimeSpan.FromMinutes(_configuration.GetValue<int>("BotSettings:SyncIntervalMinutes", 10));
        private int DashboardDays => _configuration.GetValue<int>("BotSettings:DashboardDays", 12);

        public MatchSyncWorker(
            IDbContextFactory<AppDbContext> dbContextFactory,
            ILogger<MatchSyncWorker> logger,
            FootballApiService apiService,
            DiscordSocketClient discordClient,
            IConfiguration configuration,
            IScoringService scoringService)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _apiService = apiService;
            _discordClient = discordClient;
            _configuration = configuration;
            _scoringService = scoringService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Match Sync Worker is starting.");

            // Wait for Discord client to be ready
            while (_discordClient.ConnectionState != ConnectionState.Connected)
            {
                await Task.Delay(1000, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Syncing matches from football API...");
                    await SyncMatchesAsync();
                    await ProcessFinishedMatchesAsync();
                    
                    // Update persistent dashboards
                    await UpdateLeaderboardDashboardAsync();
                    await UpdateDailyMatchesDashboardAsync();

                    // Check for scheduled match reminder notifications
                    await CheckAndSendBetNotificationsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during match sync or dashboard update.");
                }

                await Task.Delay(SyncInterval, stoppingToken);
            }
        }

        private async Task SyncMatchesAsync()
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            
            var apiMatches = await _apiService.GetUpcomingMatchesAsync(CompetitionId);

            foreach (var apiMatch in apiMatches)
            {
                var existingMatch = await dbContext.Matches.FindAsync(apiMatch.Id);

                // Fetch detailed score for overtime matches to ensure we have the correct standard score
                if ((apiMatch.Status == "FINISHED" || apiMatch.Status == "AWARDED") &&
                    (apiMatch.Duration == "EXTRA_TIME" || apiMatch.Duration == "PENALTY_SHOOTOUT"))
                {
                    bool needsDetail = existingMatch == null || 
                                       existingMatch.HomeStandardScore == null || 
                                       (existingMatch.HomeStandardScore == existingMatch.HomeScore && 
                                        existingMatch.AwayStandardScore == existingMatch.AwayScore && 
                                        existingMatch.Winner != "DRAW");
                    
                    if (needsDetail)
                    {
                        var detailedMatch = await _apiService.GetMatchByIdAsync(apiMatch.Id);
                        if (detailedMatch != null)
                        {
                            apiMatch.HomeStandardScore = detailedMatch.HomeStandardScore;
                            apiMatch.AwayStandardScore = detailedMatch.AwayStandardScore;
                        }
                    }
                    else
                    {
                        // Preserve the existing correct standard scores from DB
                        apiMatch.HomeStandardScore = existingMatch!.HomeStandardScore;
                        apiMatch.AwayStandardScore = existingMatch!.AwayStandardScore;
                    }
                }

                if (existingMatch == null)
                {
                    dbContext.Matches.Add(apiMatch);
                }
                else
                {
                    if (existingMatch.Status == "TIMED" && apiMatch.Status == "IN_PLAY")
                    {
                        await BroadcastMatchBetsAsync(existingMatch.Id);
                    }

                    existingMatch.Status = apiMatch.Status;
                    existingMatch.HomeScore = apiMatch.HomeScore;
                    existingMatch.AwayScore = apiMatch.AwayScore;
                    existingMatch.Winner = apiMatch.Winner;
                    existingMatch.HomeStandardScore = apiMatch.HomeStandardScore;
                    existingMatch.AwayStandardScore = apiMatch.AwayStandardScore;
                }
            }

            await dbContext.SaveChangesAsync();
        }

        private async Task BroadcastMatchBetsAsync(int matchId)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var match = await dbContext.Matches
                .Include(m => m.Bets)
                .ThenInclude(b => b.User)
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null) return;

            var channelIdStr = _configuration["NotificationChannelId"];
            if (!ulong.TryParse(channelIdStr, out var channelId) || channelId == 0) return;

            var channel = await _discordClient.GetChannelAsync(channelId) as IMessageChannel;
            if (channel == null) return;

            var embed = new EmbedBuilder()
                .WithTitle($"⚽ Match Started: {FlagHelper.GetFlag(match.HomeTeam)} {match.HomeTeam} vs {match.AwayTeam} {FlagHelper.GetFlag(match.AwayTeam)}")
                .WithDescription("Betting has closed. Here are all placed predictions:")
                .WithColor(Color.Orange);

            if (match.Bets.Any())
            {
                foreach (var bet in match.Bets)
                {
                    string betDetails = $"**{bet.PredictedHomeScore}:{bet.PredictedAwayScore}**";
                    if (bet.PredictedHomeScore == bet.PredictedAwayScore && bet.PredictedWinner.HasValue)
                    {
                        string winnerTeam = bet.PredictedWinner == 1 ? match.HomeTeam : match.AwayTeam;
                        string winnerFlag = FlagHelper.GetFlag(winnerTeam);
                        string winnerName = FlagHelper.GetName(winnerTeam);
                        betDetails += $" (advances: {winnerFlag} {winnerName})";
                    }

                    if (embed.Fields.Count < 25)
                    {
                        embed.AddField(bet.User.Username, $"Bet: {betDetails}", inline: true);
                    }
                    else if (embed.Fields.Count == 25)
                    {
                        embed.WithFooter($"...and {match.Bets.Count - 25} more predictions.");
                    }
                }
            }
            else
            {
                embed.WithDescription("No bets were placed on this match.");
            }

            await channel.SendMessageAsync(embed: embed.Build());
        }

        private async Task ProcessFinishedMatchesAsync()
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var finishedMatches = await dbContext.Matches
                .Where(m => (m.Status == "FINISHED" || m.Status == "AWARDED") && m.Bets.Any(b => !b.IsProcessed))
                .Include(m => m.Bets)
                .ThenInclude(b => b.User)
                .ToListAsync();

            if (finishedMatches.Any())
            {
                _logger.LogInformation("Found {Count} finished matches to process.", finishedMatches.Count);
            }

            foreach (var match in finishedMatches)
            {
                if (match.HomeScore == null || match.AwayScore == null)
                {
                    _logger.LogWarning("Match {Id} ({Home} vs {Away}) marked as {Status} but score is missing.", 
                        match.Id, match.HomeTeam, match.AwayTeam, match.Status);
                    continue;
                }

                string scoreText = $"{match.HomeScore}:{match.AwayScore}";
                if (match.HomeStandardScore.HasValue && match.AwayStandardScore.HasValue && 
                    (match.HomeStandardScore != match.HomeScore || match.AwayStandardScore != match.AwayScore))
                {
                    scoreText = $"{match.HomeStandardScore}:{match.AwayStandardScore} (final {match.HomeScore}:{match.AwayScore})";
                }

                var resultEmbed = new EmbedBuilder()
                    .WithTitle($"🏁 Match Result: {FlagHelper.GetFlag(match.HomeTeam)} {match.HomeTeam} {scoreText} {match.AwayTeam} {FlagHelper.GetFlag(match.AwayTeam)}")
                    .WithColor(Color.Green);

                string betsSummary = "";
                foreach (var bet in match.Bets.Where(b => !b.IsProcessed))
                {
                    int points = _scoringService.CalculatePoints(
                        match.HomeStandardScore ?? match.HomeScore.Value,
                        match.AwayStandardScore ?? match.AwayScore.Value,
                        match.Winner,
                        bet.PredictedHomeScore,
                        bet.PredictedAwayScore,
                        bet.PredictedWinner);
                    bet.PointsAwarded = points;
                    bet.IsProcessed = true;
                    bet.User.Points += points;
                    
                    string betDetails = $"**{bet.PredictedHomeScore}:{bet.PredictedAwayScore}**";
                    if (bet.PredictedHomeScore == bet.PredictedAwayScore && bet.PredictedWinner.HasValue)
                    {
                        string wTeam = bet.PredictedWinner == 1 ? match.HomeTeam : match.AwayTeam;
                        betDetails += $" ({FlagHelper.GetFlag(wTeam)})";
                    }

                    betsSummary += $"• **{bet.User.Username}**: Bet {betDetails} -> **+{points} pts**\n";
                }

                resultEmbed.WithDescription(string.IsNullOrEmpty(betsSummary) ? "No bets to evaluate." : betsSummary);
                await SendToChannelAsync("ResultsChannelId", resultEmbed.Build());
            }

            await dbContext.SaveChangesAsync();
        }

        private async Task UpdateLeaderboardDashboardAsync()
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var topUsers = await dbContext.Users
                .OrderByDescending(u => u.Points)
                .Take(20)
                .ToListAsync();

            var embed = new EmbedBuilder()
                .WithTitle("🏆 LEADERBOARD - TOP CONTESTANTS")
                .WithColor(Color.Gold)
                .WithTimestamp(DateTimeOffset.Now);

            string description = "";
            for (int i = 0; i < topUsers.Count; i++)
            {
                string medal = i switch { 0 => "🥇 ", 1 => "🥈 ", 2 => "🥉 ", _ => $"{i + 1}. " };
                description += $"{medal}**{topUsers[i].Username}** — {topUsers[i].Points} pts\n";
            }

            embed.WithDescription(string.IsNullOrEmpty(description) ? "No points recorded yet." : description);

            await MaintainPersistentMessageAsync("LeaderboardChannelId", "LeaderboardMessageId", embed.Build());
        }

        private async Task UpdateDailyMatchesDashboardAsync()
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var upcomingMatches = await dbContext.Matches
                .Where(m => m.KickOffTime > DateTime.UtcNow && m.KickOffTime < DateTime.UtcNow.AddDays(DashboardDays) && m.Status == "TIMED")
                .OrderBy(m => m.KickOffTime)
                .Take(8)
                .ToListAsync();

            var embed = new EmbedBuilder()
                .WithTitle("📅 UPCOMING MATCHES - OVERVIEW")
                .WithDescription("Click the button below to view or place your predictions.")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.Now);

            var components = new ComponentBuilder();

            // Main action button (blue)
            components.WithButton(
                label: "📋 MY BETS (Place / Edit)", 
                customId: "show_my_bets_btn",
                style: ButtonStyle.Primary,
                row: 0
            );

            if (!upcomingMatches.Any())
            {
                embed.WithDescription($"No matches scheduled in the next {DashboardDays} days.");
            }
            else
            {
                for (int i = 0; i < upcomingMatches.Count; i++)
                {
                    var match = upcomingMatches[i];
                    string homeFlag = FlagHelper.GetFlag(match.HomeTeam);
                    string awayFlag = FlagHelper.GetFlag(match.AwayTeam);
                    string homeName = FlagHelper.GetName(match.HomeTeam);
                    string awayName = FlagHelper.GetName(match.AwayTeam);

                    embed.AddField(
                        $"{homeFlag} {homeName} vs {awayName} {awayFlag}",
                        $"⏰ <t:{((DateTimeOffset)match.KickOffTime).ToUnixTimeSeconds()}:f>\n───────────────────",
                        inline: false
                    );
                }
            }

            await MaintainPersistentMessageAsync("DailyMatchesChannelId", "DailyMatchesMessageId", embed.Build(), components.Build());
        }

        private async Task MaintainPersistentMessageAsync(string channelConfigKey, string messageIdConfigKey, Embed embed, MessageComponent? components = null)
        {
            var channelIdStr = _configuration[channelConfigKey];
            if (string.IsNullOrEmpty(channelIdStr) || !ulong.TryParse(channelIdStr, out var channelId) || channelId == 0) return;

            var channel = await _discordClient.GetChannelAsync(channelId) as ITextChannel;
            if (channel == null) return;

            ulong messageId = 0;
            await using (var dbContext = await _dbContextFactory.CreateDbContextAsync())
            {
                var setting = await dbContext.BotSettings.FindAsync(messageIdConfigKey);
                if (setting != null && ulong.TryParse(setting.Value, out var savedId))
                {
                    messageId = savedId;
                }
                else
                {
                    var messageIdStr = _configuration[messageIdConfigKey];
                    if (!string.IsNullOrEmpty(messageIdStr)) ulong.TryParse(messageIdStr, out messageId);
                }
            }

            IUserMessage? message = null;
            if (messageId != 0)
            {
                try
                {
                    message = await channel.GetMessageAsync(messageId) as IUserMessage;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch existing persistent message {MessageId}", messageId);
                }
            }

            if (message == null)
            {
                // Clean channel of other messages to keep it tidy if permitted
                try
                {
                    var oldMessages = await channel.GetMessagesAsync(10).FlattenAsync();
                    foreach (var oldMsg in oldMessages) await oldMsg.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not clean old messages in channel {ChannelId}", channel.Id);
                }

                message = await channel.SendMessageAsync(embed: embed, components: components);
                _logger.LogInformation("Created persistent message in {Key}: {MessageId}", channelConfigKey, message.Id);

                await using (var dbContext = await _dbContextFactory.CreateDbContextAsync())
                {
                    var setting = await dbContext.BotSettings.FindAsync(messageIdConfigKey);
                    if (setting == null)
                    {
                        dbContext.BotSettings.Add(new BotSetting { Key = messageIdConfigKey, Value = message.Id.ToString() });
                    }
                    else
                    {
                        setting.Value = message.Id.ToString();
                    }
                    await dbContext.SaveChangesAsync();
                }
            }
            else
            {
                await message.ModifyAsync(msg => {
                    msg.Embed = embed;
                    msg.Components = components;
                });
            }
        }

        private async Task SendToChannelAsync(string configKey, Embed embed)
        {
            var channelIdStr = _configuration[configKey];
            if (string.IsNullOrEmpty(channelIdStr) || !ulong.TryParse(channelIdStr, out var channelId) || channelId == 0) return;

            var channel = await _discordClient.GetChannelAsync(channelId) as IMessageChannel;
            if (channel != null)
            {
                await channel.SendMessageAsync(embed: embed);
            }
        }

        private TimeZoneInfo GetPragueTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
                }
                catch
                {
                    // Fallback to static offset for Central Europe Summer Time (+2) if timezone IDs fail
                    return TimeZoneInfo.CreateCustomTimeZone("PragueTemp", TimeSpan.FromHours(2), "Prague Time", "Prague Time");
                }
            }
        }

        private async Task CheckAndSendBetNotificationsAsync()
        {
            var tz = GetPragueTimeZone();
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

            // 15:00 Reminder (for matches from 18:00 today until midnight local time)
            if (now.Hour == 15 && _last15NotificationDate != now.Date)
            {
                var rangeStartLocal = now.Date.AddHours(18);
                var rangeEndLocal = now.Date.AddDays(1);
                var rangeStart = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, tz);
                var rangeEnd = TimeZoneInfo.ConvertTimeToUtc(rangeEndLocal, tz);

                _logger.LogInformation("Running 15:00 notification check for range {Start} to {End} (UTC)", rangeStart, rangeEnd);
                await ProcessNotificationsForRangeAsync(rangeStart, rangeEnd);
                _last15NotificationDate = now.Date;
            }
            // 22:00 Reminder (for matches from midnight tomorrow until 06:00 tomorrow local time)
            else if (now.Hour == 22 && _last22NotificationDate != now.Date)
            {
                var rangeStartLocal = now.Date.AddDays(1);
                var rangeEndLocal = now.Date.AddDays(1).AddHours(6);
                var rangeStart = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, tz);
                var rangeEnd = TimeZoneInfo.ConvertTimeToUtc(rangeEndLocal, tz);

                _logger.LogInformation("Running 22:00 notification check for range {Start} to {End} (UTC)", rangeStart, rangeEnd);
                await ProcessNotificationsForRangeAsync(rangeStart, rangeEnd);
                _last22NotificationDate = now.Date;
            }
        }

        private async Task ProcessNotificationsForRangeAsync(DateTime rangeStartUtc, DateTime rangeEndUtc)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var matchesInRange = await dbContext.Matches
                .Where(m => m.KickOffTime >= rangeStartUtc && m.KickOffTime < rangeEndUtc && m.Status == "TIMED")
                .ToListAsync();

            if (!matchesInRange.Any())
            {
                _logger.LogInformation("No matches found in the notification range.");
                return;
            }

            var users = await dbContext.Users.ToListAsync();
            var usersToPing = new List<ulong>();

            var matchIds = matchesInRange.Select(m => m.Id).ToList();
            var existingBets = await dbContext.Bets
                .Where(b => matchIds.Contains(b.MatchId))
                .Select(b => new { b.UserId, b.MatchId })
                .ToListAsync();

            var userBetsLookup = existingBets
                .GroupBy(b => b.UserId)
                .ToDictionary(g => g.Key, g => g.Select(b => b.MatchId).ToHashSet());

            foreach (var user in users)
            {
                userBetsLookup.TryGetValue(user.DiscordId, out var placedMatchIds);
                placedMatchIds ??= new HashSet<int>();

                bool missingAnyBet = matchesInRange.Any(m => !placedMatchIds.Contains(m.Id));
                if (missingAnyBet)
                {
                    usersToPing.Add(user.DiscordId);
                }
            }

            if (usersToPing.Any())
            {
                var channelIdStr = _configuration["NotificationChannelId"];
                if (ulong.TryParse(channelIdStr, out var channelId) && channelId != 0)
                {
                    var channel = await _discordClient.GetChannelAsync(channelId) as ITextChannel;
                    if (channel != null)
                    {
                        string pings = string.Join(" ", usersToPing.Select(id => $"<@{id}>"));
                        
                        var embed = new EmbedBuilder()
                            .WithTitle("🔔 Match Betting Reminder")
                            .WithDescription("Some of you haven't placed predictions on upcoming matches! Remember to submit your bets via the dashboard buttons.\n\n" +
                                "**Upcoming Matches:**\n" +
                                string.Join("\n", matchesInRange.Select(m => $"• {FlagHelper.GetFlag(m.HomeTeam)} {m.HomeTeam} vs {m.AwayTeam} {FlagHelper.GetFlag(m.AwayTeam)} (<t:{((DateTimeOffset)m.KickOffTime).ToUnixTimeSeconds()}:t>)")))
                            .WithColor(Color.Red)
                            .WithFooter("Predictions close promptly at kickoff!")
                            .WithTimestamp(DateTimeOffset.Now)
                            .Build();

                        await channel.SendMessageAsync(text: $"⚠️ {pings}", embed: embed);
                        _logger.LogInformation("Sent bet reminders to {Count} users.", usersToPing.Count);
                    }
                }
            }
            else
            {
                _logger.LogInformation("All users have placed their bets for the matches in this range.");
            }
        }

        // Backwards compatibility helper
        public static int CalculatePoints(
            int homeRealStandard, int awayRealStandard, string? realWinnerStr,
            int homePred, int awayPred, int? predWinnerOpt)
        {
            return new ScoringService().CalculatePoints(
                homeRealStandard, awayRealStandard, realWinnerStr, homePred, awayPred, predWinnerOpt);
        }
    }
}
