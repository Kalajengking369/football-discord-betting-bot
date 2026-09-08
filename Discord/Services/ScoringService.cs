namespace Discord.Services
{
    public class ScoringService : IScoringService
    {
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

            // 1. Exact standard time score (4 pts, +1 pt if draw and correct playoff winner = 5 pts)
            if (homeRealStandard == homePred && awayRealStandard == awayPred)
            {
                int points = 4;
                if (homeRealStandard == awayRealStandard)
                {
                    if (predWinner == realWinner && realWinner != 0)
                    {
                        points += 1;
                    }
                }
                return points;
            }

            // 2. Standard time ended in a draw
            if (homeRealStandard == awayRealStandard)
            {
                // User predicted a draw (2 pts, +1 pt if correct playoff winner = 3 pts)
                if (homePred == awayPred)
                {
                    int points = 2;
                    if (predWinner == realWinner && realWinner != 0)
                    {
                        points += 1;
                    }
                    return points;
                }
                else
                {
                    // User predicted a team win, but standard time was a draw, and that team won in overtime
                    if (predWinner == realWinner && realWinner != 0)
                    {
                        return 1;
                    }
                    return 0;
                }
            }

            // 3. Standard time ended in a team win
            if (homeRealStandard != awayRealStandard)
            {
                // User predicted win for the correct team (2 pts)
                if ((homeRealStandard > awayRealStandard && homePred > awayPred) ||
                    (homeRealStandard < awayRealStandard && homePred < awayPred))
                {
                    return 2;
                }

                // User predicted draw, but their selected team won in regular time (1 pt)
                if (homePred == awayPred && predWinner == realWinner && realWinner != 0)
                {
                    return 1;
                }
            }

            return 0;
        }
    }
}
