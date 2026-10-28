using Microsoft.Extensions.Configuration;

namespace Discord.Services
{
    public class ScoringService : IScoringService
    {
        public int ExactScorePoints { get; } = 4;
        public int WinnerPoints { get; } = 2;
        public int DrawPoints { get; } = 2;
        public int PlayoffBonusPoints { get; } = 1;
        public int OvertimeBonusPoints { get; } = 1;

        public ScoringService()
        {
        }

        public ScoringService(IConfiguration? configuration)
        {
            if (configuration != null)
            {
                ExactScorePoints = configuration.GetValue("Scoring:ExactScorePoints", 4);
                WinnerPoints = configuration.GetValue("Scoring:WinnerPoints", 2);
                DrawPoints = configuration.GetValue("Scoring:DrawPoints", 2);
                PlayoffBonusPoints = configuration.GetValue("Scoring:PlayoffBonusPoints", 1);
                OvertimeBonusPoints = configuration.GetValue("Scoring:OvertimeBonusPoints", 1);
            }
        }

        public int CalculatePoints(
            int homeRealStandard,
            int awayRealStandard,
            string? realWinnerStr,
            int homePred,
            int awayPred,
            int? predWinnerOpt)
        {
            // Determine real winner of the match: 1 = Home, 2 = Away, 0 = Draw/None
            int realWinner = 0;
            if (realWinnerStr == "HOME_TEAM") realWinner = 1;
            else if (realWinnerStr == "AWAY_TEAM") realWinner = 2;

            // Determine predicted winner
            int predWinner = 0;
            if (homePred > awayPred) predWinner = 1;
            else if (homePred < awayPred) predWinner = 2;
            else if (predWinnerOpt.HasValue) predWinner = predWinnerOpt.Value;

            // 1. Exact standard time score (e.g. 4 pts, +1 pt if draw and correct playoff winner = 5 pts)
            if (homeRealStandard == homePred && awayRealStandard == awayPred)
            {
                int points = ExactScorePoints;
                if (homeRealStandard == awayRealStandard)
                {
                    if (predWinner == realWinner && realWinner != 0)
                    {
                        points += PlayoffBonusPoints;
                    }
                }
                return points;
            }

            // 2. Standard time ended in a draw
            if (homeRealStandard == awayRealStandard)
            {
                // User predicted a draw (e.g. 2 pts, +1 pt if correct playoff winner = 3 pts)
                if (homePred == awayPred)
                {
                    int points = DrawPoints;
                    if (predWinner == realWinner && realWinner != 0)
                    {
                        points += PlayoffBonusPoints;
                    }
                    return points;
                }
                else
                {
                    // User predicted a team win, but standard time was a draw, and that team won in overtime
                    if (predWinner == realWinner && realWinner != 0)
                    {
                        return OvertimeBonusPoints;
                    }
                    return 0;
                }
            }

            // 3. Standard time ended in a team win
            if (homeRealStandard != awayRealStandard)
            {
                // User predicted win for the correct team (e.g. 2 pts)
                if ((homeRealStandard > awayRealStandard && homePred > awayPred) ||
                    (homeRealStandard < awayRealStandard && homePred < awayPred))
                {
                    return WinnerPoints;
                }

                // User predicted draw with advancing team, but that team won in regular time (e.g. 1 pt)
                if (homePred == awayPred && predWinner == realWinner && realWinner != 0)
                {
                    return OvertimeBonusPoints;
                }
            }

            return 0;
        }
    }
}
