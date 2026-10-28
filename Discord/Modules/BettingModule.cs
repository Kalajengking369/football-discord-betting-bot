using Discord.Interactions;
using Discord.WebSocket;
using Discord.Data;
using Discord.Models;
using Discord.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Discord.Modules
{
    public class BettingModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IScoringService _scoringService;
        private readonly FootballApiService _apiService;
        private readonly IConfiguration _configuration;
        private readonly DiscordSocketClient _discordClient;

        public BettingModule(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IScoringService scoringService,
            FootballApiService apiService,
            IConfiguration configuration,
            DiscordSocketClient discordClient)
        {
            _dbContextFactory = dbContextFactory;
            _scoringService = scoringService;
            _apiService = apiService;
            _configuration = configuration;
            _discordClient = discordClient;
        }

        public class BetModal : IModal
        {
            public string Title { get; set; } = "Place Prediction";
            
            [InputLabel("Goals - Home Team")]
            [ModalTextInput("home_score", placeholder: "e.g. 2", minLength: 1, maxLength: 2)]
            public string HomeScore { get; set; } = string.Empty;

            [InputLabel("Goals - Away Team")]
            [ModalTextInput("away_score", placeholder: "e.g. 1", minLength: 1, maxLength: 2)]
            public string AwayScore { get; set; } = string.Empty;
        }

        [SlashCommand("matches", "Displays upcoming matches available for betting")]
        public async Task ListMatches()
        {
            await ShowMatchesInternal(true);
        }

        [ComponentInteraction("show_my_bets_btn")]
        public async Task HandleShowMyBetsButton()
        {
            await ShowMatchesInternal(true);
        }

        private async Task ShowMatchesInternal(bool isEphemeral)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var upcomingMatches = await dbContext.Matches
                .Where(m => m.KickOffTime > DateTime.UtcNow && m.Status == "TIMED")
                .Where(m => m.HomeTeam != null && m.AwayTeam != null)
                .OrderBy(m => m.KickOffTime)
                .Take(8)
                .ToListAsync();

            if (!upcomingMatches.Any())
            {
                await RespondAsync("No upcoming matches are currently available for prediction.", ephemeral: isEphemeral);
                return;
            }

            // Retrieve current user's bets for upcoming matches
            var matchIds = upcomingMatches.Select(m => m.Id).ToList();
            var userBets = await dbContext.Bets
                .Where(b => b.UserId == Context.User.Id && matchIds.Contains(b.MatchId))
                .ToDictionaryAsync(b => b.MatchId);

            var embed = new EmbedBuilder()
                .WithTitle("🏆 Betting Menu - Upcoming Matches")
                .WithDescription("Click the button below any match to place or update your prediction.")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            var components = new ComponentBuilder();

            for (int i = 0; i < upcomingMatches.Count; i++)
            {
                var match = upcomingMatches[i];
                bool hasBet = userBets.TryGetValue(match.Id, out var bet);
                
                string homeName = FlagHelper.GetName(match.HomeTeam);
                string awayName = FlagHelper.GetName(match.AwayTeam);
                string homeFlag = FlagHelper.GetFlag(match.HomeTeam);
                string awayFlag = FlagHelper.GetFlag(match.AwayTeam);

                string betDisplay = "**X:X**";
                if (hasBet)
                {
                    betDisplay = $"**{bet!.PredictedHomeScore}:{bet.PredictedAwayScore}**";
                    if (bet.PredictedHomeScore == bet.PredictedAwayScore && bet.PredictedWinner.HasValue)
                    {
                        string winnerTeam = bet.PredictedWinner == 1 ? match.HomeTeam : match.AwayTeam;
                        string winnerFlag = FlagHelper.GetFlag(winnerTeam);
                        string winnerNameFormatted = FlagHelper.GetName(winnerTeam);
                        betDisplay += $" (advances: {winnerFlag} {winnerNameFormatted})";
                    }
                }

                embed.AddField(
                    $"{homeFlag} {homeName} vs {awayName} {awayFlag}",
                    $"Bet: {betDisplay} | ⏰ <t:{((DateTimeOffset)match.KickOffTime).ToUnixTimeSeconds()}:t>\n───────────────────",
                    inline: false
                );

                components.WithButton(
                    label: $"{homeName} - {awayName}", 
                    customId: $"tip_btn:{match.Id}",
                    style: hasBet ? ButtonStyle.Primary : ButtonStyle.Success,
                    emote: new Emoji(hasBet ? "📝" : "⚽"),
                    row: i / 2
                );
            }

            // Action button to view full betting history
            components.WithButton("All My Bets", "show_full_history_btn", ButtonStyle.Secondary, new Emoji("📊"), row: 4);

            await RespondAsync(embed: embed.Build(), components: components.Build(), ephemeral: isEphemeral);
        }

        [SlashCommand("history", "Displays betting history for you or another user")]
        public async Task ShowHistory(SocketUser? user = null)
        {
            var targetUser = user ?? Context.User;
            bool isSelf = targetUser.Id == Context.User.Id;

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var bets = await dbContext.Bets
                .Where(b => b.UserId == targetUser.Id)
                .Include(b => b.Match)
                .OrderByDescending(b => b.Match.KickOffTime)
                .Take(20)
                .ToListAsync();

            if (!bets.Any())
            {
                await RespondAsync(isSelf ? "You have not placed any bets yet." : $"User **{targetUser.Username}** has not placed any bets yet.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"📋 Betting History: {targetUser.Username}")
                .WithColor(isSelf ? Color.Teal : Color.LightGrey)
                .WithFooter(isSelf ? "" : "Predictions for future matches are hidden until kickoff to ensure fairness.");

            foreach (var bet in bets)
            {
                bool matchStarted = bet.Match.KickOffTime <= DateTime.UtcNow;
                string homeName = FlagHelper.GetName(bet.Match.HomeTeam);
                string awayName = FlagHelper.GetName(bet.Match.AwayTeam);
                string homeFlag = FlagHelper.GetFlag(bet.Match.HomeTeam);
                string awayFlag = FlagHelper.GetFlag(bet.Match.AwayTeam);

                string betDisplay;
                if (isSelf || matchStarted)
                {
                    betDisplay = $"**{bet.PredictedHomeScore}:{bet.PredictedAwayScore}**";
                    if (bet.PredictedHomeScore == bet.PredictedAwayScore && bet.PredictedWinner.HasValue)
                    {
                        string winnerTeam = bet.PredictedWinner == 1 ? bet.Match.HomeTeam : bet.Match.AwayTeam;
                        string winnerFlag = FlagHelper.GetFlag(winnerTeam);
                        string winnerNameFormatted = FlagHelper.GetName(winnerTeam);
                        betDisplay += $" (advances: {winnerFlag} {winnerNameFormatted})";
                    }
                }
                else
                {
                    betDisplay = "❓ **Hidden (until kickoff)**";
                }

                string status = matchStarted ? "⚽ Match in progress or finished" : $"⏳ Kickoff <t:{((DateTimeOffset)bet.Match.KickOffTime).ToUnixTimeSeconds()}:R>";
                string points = bet.IsProcessed ? $" | Points: **{bet.PointsAwarded}**" : "";
                string matchIdInfo = $" | ID: `{bet.MatchId}`";

                embed.AddField(
                    $"{homeFlag} {homeName} vs {awayName} {awayFlag}",
                    $"Bet: {betDisplay}\nStatus: {status}{points}{matchIdInfo}",
                    inline: false
                );
            }

            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }

        [ComponentInteraction("show_full_history_btn")]
        public async Task HandleFullHistoryButton()
        {
            await ShowHistory(null);
        }

        [ComponentInteraction("tip_btn:*")]
        public async Task HandleTipButton(string matchIdStr)
        {
            if (!int.TryParse(matchIdStr, out int matchId)) return;

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            
            var match = await dbContext.Matches.FindAsync(matchId);
            if (match == null) return;

            string homeFlag = FlagHelper.GetFlag(match.HomeTeam);
            string awayFlag = FlagHelper.GetFlag(match.AwayTeam);
            string homeName = FlagHelper.GetName(match.HomeTeam);
            string awayName = FlagHelper.GetName(match.AwayTeam);

            // Find existing bet for the user if already placed
            var existingBet = await dbContext.Bets
                .FirstOrDefaultAsync(b => b.MatchId == matchId && b.UserId == Context.User.Id);

            var mb = new ModalBuilder()
                .WithTitle($"Prediction: {homeName} vs {awayName}")
                .WithCustomId($"bet_modal:{matchId}")
                .AddTextInput(
                    label: $"{homeFlag} {homeName}", 
                    customId: "home_score", 
                    placeholder: "Score", 
                    value: existingBet?.PredictedHomeScore.ToString(),
                    minLength: 1, 
                    maxLength: 2, 
                    required: true)
                .AddTextInput(
                    label: $"{awayFlag} {awayName}", 
                    customId: "away_score", 
                    placeholder: "Score", 
                    value: existingBet?.PredictedAwayScore.ToString(),
                    minLength: 1, 
                    maxLength: 2, 
                    required: true);

            await RespondWithModalAsync(mb.Build());
        }

        [ModalInteraction("bet_modal:*")]
        public async Task HandleBetModal(string matchIdStr, BetModal modal)
        {
            if (!int.TryParse(matchIdStr, out int matchId) || 
                !int.TryParse(modal.HomeScore, out int homeScore) || 
                !int.TryParse(modal.AwayScore, out int awayScore) ||
                homeScore < 0 || awayScore < 0 || homeScore > 99 || awayScore > 99)
            {
                await RespondAsync("Invalid score format. Please enter non-negative integers (0-99).", ephemeral: true);
                return;
            }

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var match = await dbContext.Matches.FindAsync(matchId);
            if (match == null)
            {
                await RespondAsync("Match not found.", ephemeral: true);
                return;
            }

            if (match.KickOffTime <= DateTime.UtcNow)
            {
                await RespondAsync("Betting for this match has already closed.", ephemeral: true);
                return;
            }

            var user = await dbContext.Users.FindAsync(Context.User.Id);
            if (user == null)
            {
                user = new User { DiscordId = Context.User.Id, Username = Context.User.Username };
                dbContext.Users.Add(user);
            }

            var existingBet = await dbContext.Bets
                .FirstOrDefaultAsync(b => b.MatchId == matchId && b.UserId == Context.User.Id);

            string homeFlag = FlagHelper.GetFlag(match.HomeTeam);
            string awayFlag = FlagHelper.GetFlag(match.AwayTeam);
            string homeName = FlagHelper.GetName(match.HomeTeam);
            string awayName = FlagHelper.GetName(match.AwayTeam);

            if (homeScore != awayScore)
            {
                int predictedWinner = (homeScore > awayScore) ? 1 : 2;
                if (existingBet != null)
                {
                    existingBet.PredictedHomeScore = homeScore;
                    existingBet.PredictedAwayScore = awayScore;
                    existingBet.PredictedWinner = predictedWinner;
                    await RespondAsync($"Your bet on **{homeFlag} {homeName} vs {awayName} {awayFlag}** was updated to **{homeScore}:{awayScore}**.", ephemeral: true);
                }
                else
                {
                    var bet = new Bet
                    {
                        MatchId = matchId,
                        UserId = Context.User.Id,
                        PredictedHomeScore = homeScore,
                        PredictedAwayScore = awayScore,
                        PredictedWinner = predictedWinner
                    };
                    dbContext.Bets.Add(bet);
                    await RespondAsync($"Your bet on **{homeFlag} {homeName} vs {awayName} {awayFlag}** has been placed: **{homeScore}:{awayScore}**.", ephemeral: true);
                }
                await dbContext.SaveChangesAsync();
            }
            else
            {
                Emoji? homeEmoji = null;
                Emoji? awayEmoji = null;
                try
                {
                    if (!string.IsNullOrEmpty(homeFlag)) homeEmoji = new Emoji(homeFlag);
                }
                catch {}
                try
                {
                    if (!string.IsNullOrEmpty(awayFlag)) awayEmoji = new Emoji(awayFlag);
                }
                catch {}

                var components = new ComponentBuilder()
                    .WithButton($"{homeName} (Advances)", $"select_winner:{matchId}:{homeScore}:{awayScore}:1", ButtonStyle.Primary, homeEmoji, row: 0)
                    .WithButton($"{awayName} (Advances)", $"select_winner:{matchId}:{homeScore}:{awayScore}:2", ButtonStyle.Danger, awayEmoji, row: 0)
                    .WithButton("Regular Draw (No Overtime)", $"select_winner:{matchId}:{homeScore}:{awayScore}:0", ButtonStyle.Secondary, new Emoji("🤝"), row: 1);

                await RespondAsync($"You predicted a draw **{homeScore}:{awayScore}**. Select who advances (for knockout/playoff matches), or choose **Regular Draw**:", components: components.Build(), ephemeral: true);
            }
        }

        [ComponentInteraction("select_winner:*:*:*:*")]
        public async Task HandleSelectWinner(string matchIdStr, string homeScoreStr, string awayScoreStr, string winnerStr)
        {
            if (!int.TryParse(matchIdStr, out int matchId) || 
                !int.TryParse(homeScoreStr, out int homeScore) || 
                !int.TryParse(awayScoreStr, out int awayScore) ||
                !int.TryParse(winnerStr, out int winnerVal) ||
                homeScore < 0 || awayScore < 0 || homeScore > 99 || awayScore > 99)
            {
                await RespondAsync("Invalid action.", ephemeral: true);
                return;
            }

            int? winner = (winnerVal == 1 || winnerVal == 2) ? winnerVal : null;

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var match = await dbContext.Matches.FindAsync(matchId);
            if (match == null)
            {
                await RespondAsync("Match not found.", ephemeral: true);
                return;
            }

            if (match.KickOffTime <= DateTime.UtcNow)
            {
                await RespondAsync("Betting for this match has already closed.", ephemeral: true);
                return;
            }

            var user = await dbContext.Users.FindAsync(Context.User.Id);
            if (user == null)
            {
                user = new User { DiscordId = Context.User.Id, Username = Context.User.Username };
                dbContext.Users.Add(user);
            }

            var existingBet = await dbContext.Bets
                .FirstOrDefaultAsync(b => b.MatchId == matchId && b.UserId == Context.User.Id);

            string homeFlag = FlagHelper.GetFlag(match.HomeTeam);
            string awayFlag = FlagHelper.GetFlag(match.AwayTeam);
            string homeName = FlagHelper.GetName(match.HomeTeam);
            string awayName = FlagHelper.GetName(match.AwayTeam);

            if (existingBet != null)
            {
                existingBet.PredictedHomeScore = homeScore;
                existingBet.PredictedAwayScore = awayScore;
                existingBet.PredictedWinner = winner;
            }
            else
            {
                var bet = new Bet
                {
                    MatchId = matchId,
                    UserId = Context.User.Id,
                    PredictedHomeScore = homeScore,
                    PredictedAwayScore = awayScore,
                    PredictedWinner = winner
                };
                dbContext.Bets.Add(bet);
            }

            await dbContext.SaveChangesAsync();

            string statusText = " (Regular Draw)";
            if (winner.HasValue)
            {
                string winnerTeamName = (winner.Value == 1) ? match.HomeTeam : match.AwayTeam;
                string winnerFlag = FlagHelper.GetFlag(winnerTeamName);
                string winnerNameFormatted = FlagHelper.GetName(winnerTeamName);
                statusText = $" ({winnerFlag} {winnerNameFormatted} to advance)";
            }

            string savedMessage = $"Your bet on **{homeFlag} {homeName} vs {awayName} {awayFlag}** has been saved: **{homeScore}:{awayScore}**{statusText}.";

            if (Context.Interaction is SocketMessageComponent componentInteraction)
            {
                await componentInteraction.UpdateAsync(msg => {
                    msg.Content = savedMessage;
                    msg.Components = new ComponentBuilder().Build();
                });
            }
            else
            {
                await RespondAsync(savedMessage, ephemeral: true);
            }
        }

        [SlashCommand("mybets", "Displays your upcoming predictions")]
        public async Task ShowMyBets()
        {
            await ShowMatchesInternal(true);
        }

        [SlashCommand("leaderboard", "Displays top contestants on the leaderboard")]
        public async Task ShowLeaderboard()
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var topUsers = await dbContext.Users
                .OrderByDescending(u => u.Points)
                .Take(10)
                .ToListAsync();

            if (!topUsers.Any())
            {
                await RespondAsync("The leaderboard is currently empty.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("📊 Leaderboard - Top Contestants")
                .WithColor(Color.Gold);

            for (int i = 0; i < topUsers.Count; i++)
            {
                var user = topUsers[i];
                embed.Description += $"{i + 1}. **{user.Username}** - {user.Points} pts\n";
            }

            await RespondAsync(embed: embed.Build());
        }

        [SlashCommand("help", "Displays bot help and scoring rules")]
        public async Task ShowHelp()
        {
            var embed = new EmbedBuilder()
                .WithTitle("📖 Help - Football Betting Bot")
                .WithDescription("Welcome to the football betting contest! Here is the list of available commands:")
                .AddField("/matches", "Browse upcoming matches and place your predictions.")
                .AddField("/history", "View your betting history (or inspect another user's public bets).")
                .AddField("/leaderboard", "View current rankings and standings.")
                .AddField("Scoring System",
                    $"• **{_scoringService.ExactScorePoints} points**: Exact score at regular time (90 min)\n" +
                    $"• **{_scoringService.WinnerPoints} points**: Correct winner with a different score\n" +
                    $"• **{_scoringService.DrawPoints} points**: Predicted draw and match ended in a draw (90 min)\n" +
                    $"• **+{_scoringService.PlayoffBonusPoints} point**: Added to draw predictions for correctly guessing who advances in playoffs\n" +
                    $"• **{_scoringService.OvertimeBonusPoints} point**: Predicted team win, but that team won in extra time / penalties\n" +
                    $"• **{_scoringService.OvertimeBonusPoints} point**: Predicted draw with advancement pick, but that team won in regular time\n" +
                    "• **0 points**: Incorrect prediction")
                .WithColor(Color.Green)
                .WithFooter("Predictions close automatically at exact match kickoff.");

            await RespondAsync(embed: embed.Build(), ephemeral: true);
        }

        [SlashCommand("recalc-match", "Recalculates points and updates match score for a completed match")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task RecalculateMatch(int id)
        {
            await DeferAsync(ephemeral: true);
            
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var match = await dbContext.Matches
                .Include(m => m.Bets)
                .ThenInclude(b => b.User)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (match == null)
            {
                await FollowupAsync("Match not found in database.", ephemeral: true);
                return;
            }

            if (match.Status != "FINISHED" && match.Status != "AWARDED")
            {
                await FollowupAsync("Only completed matches can be recalculated.", ephemeral: true);
                return;
            }

            // Fetch latest match details from API to get correct regularTime score
            var detailedMatch = await _apiService.GetMatchByIdAsync(id);
            if (detailedMatch != null)
            {
                match.HomeStandardScore = detailedMatch.HomeStandardScore;
                match.AwayStandardScore = detailedMatch.AwayStandardScore;
                match.HomeScore = detailedMatch.HomeScore;
                match.AwayScore = detailedMatch.AwayScore;
                match.Winner = detailedMatch.Winner;
                match.Status = detailedMatch.Status;
            }

            if (match.HomeScore == null || match.AwayScore == null)
            {
                await FollowupAsync("Official match score is not available yet. Cannot recalculate.", ephemeral: true);
                return;
            }

            // Prepare public results embed
            string scoreText = $"{match.HomeScore}:{match.AwayScore}";
            if (match.HomeStandardScore.HasValue && match.AwayStandardScore.HasValue && 
                (match.HomeStandardScore != match.HomeScore || match.AwayStandardScore != match.AwayScore))
            {
                scoreText = $"{match.HomeStandardScore}:{match.AwayStandardScore} (final {match.HomeScore}:{match.AwayScore})";
            }

            var resultEmbed = new EmbedBuilder()
                .WithTitle($"🔄 Match Correction/Recalculation: {FlagHelper.GetFlag(match.HomeTeam)} {match.HomeTeam} {scoreText} {match.AwayTeam} {FlagHelper.GetFlag(match.AwayTeam)}")
                .WithColor(Color.Blue);

            string betsSummary = "";
            int updatedBetsCount = 0;
            foreach (var bet in match.Bets)
            {
                int oldPoints = bet.PointsAwarded ?? 0;
                int newPoints = _scoringService.CalculatePoints(
                    match.HomeStandardScore ?? match.HomeScore!.Value,
                    match.AwayStandardScore ?? match.AwayScore!.Value,
                    match.Winner,
                    bet.PredictedHomeScore,
                    bet.PredictedAwayScore,
                    bet.PredictedWinner);

                if (oldPoints != newPoints || !bet.IsProcessed)
                {
                    bet.User.Points = bet.User.Points - oldPoints + newPoints;
                    bet.PointsAwarded = newPoints;
                    bet.IsProcessed = true;
                    updatedBetsCount++;
                }

                string betDetails = $"**{bet.PredictedHomeScore}:{bet.PredictedAwayScore}**";
                if (bet.PredictedHomeScore == bet.PredictedAwayScore && bet.PredictedWinner.HasValue)
                {
                    string wTeam = bet.PredictedWinner == 1 ? match.HomeTeam : match.AwayTeam;
                    betDetails += $" ({FlagHelper.GetFlag(wTeam)})";
                }

                string diffText = "";
                if (oldPoints != newPoints)
                {
                    int diff = newPoints - oldPoints;
                    diffText = $" (corrected from **{oldPoints} pts**, diff **{(diff > 0 ? "+" : "")}{diff} pts**)";
                }

                betsSummary += $"• **{bet.User.Username}**: Bet {betDetails} -> **+{newPoints} pts**{diffText}\n";
            }

            resultEmbed.WithDescription(string.IsNullOrEmpty(betsSummary) ? "No bets to evaluate." : betsSummary);

            await dbContext.SaveChangesAsync();

            // Send to results channel
            var channelIdStr = _configuration["ResultsChannelId"];
            if (ulong.TryParse(channelIdStr, out var channelId) && channelId != 0)
            {
                var channel = await _discordClient.GetChannelAsync(channelId) as IMessageChannel;
                if (channel != null)
                {
                    await channel.SendMessageAsync(embed: resultEmbed.Build());
                }
            }

            await FollowupAsync($"Match **{match.HomeTeam} vs {match.AwayTeam}** was successfully recalculated. Updated **{updatedBetsCount}** bets and posted update to the results channel.", ephemeral: true);
        }
    }
}
