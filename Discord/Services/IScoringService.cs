namespace Discord.Services
{
    public interface IScoringService
    {
        int ExactScorePoints { get; }
        int WinnerPoints { get; }
        int DrawPoints { get; }
        int PlayoffBonusPoints { get; }
        int OvertimeBonusPoints { get; }

        /// <summary>
        /// Calculates points awarded for a match bet based on standard time score, 
        /// overtime result, and playoff advancement predictions.
        /// </summary>
        /// <param name="homeRealStandard">Actual home score in standard time (90 min)</param>
        /// <param name="awayRealStandard">Actual away score in standard time (90 min)</param>
        /// <param name="realWinnerStr">API winner string: "HOME_TEAM", "AWAY_TEAM", or "DRAW"/null</param>
        /// <param name="homePred">Predicted home score</param>
        /// <param name="awayPred">Predicted away score</param>
        /// <param name="predWinnerOpt">1 for Home, 2 for Away if user chose a winner on a draw prediction</param>
        /// <returns>Points earned</returns>
        int CalculatePoints(
            int homeRealStandard,
            int awayRealStandard,
            string? realWinnerStr,
            int homePred,
            int awayPred,
            int? predWinnerOpt);
    }
}
