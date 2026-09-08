using Xunit;
using Discord.Services;

namespace Discord.Tests
{
    public class ScoringServiceTests
    {
        private readonly IScoringService _scoring = new ScoringService();

        [Theory]
        [InlineData(2, 1, "HOME_TEAM", 2, 1, null, 4)]  // Exact regular win (4 pts)
        [InlineData(0, 3, "AWAY_TEAM", 0, 3, null, 4)]  // Exact away win (4 pts)
        public void CalculatePoints_ExactRegularScore_Awards4Points(
            int homeReal, int awayReal, string? winner,
            int homePred, int awayPred, int? predWinner, int expected)
        {
            var points = _scoring.CalculatePoints(homeReal, awayReal, winner, homePred, awayPred, predWinner);
            Assert.Equal(expected, points);
        }

        [Fact]
        public void CalculatePoints_ExactDraw_WithCorrectPlayoffWinner_Awards5Points()
        {
            // Match ended 1:1, user predicted 1:1 and selected Home to advance (1). Home won in overtime/pens.
            var points = _scoring.CalculatePoints(1, 1, "HOME_TEAM", 1, 1, 1);
            Assert.Equal(5, points); // 4 for exact draw + 1 for correct winner = 5
        }

        [Fact]
        public void CalculatePoints_ExactDraw_WithWrongPlayoffWinner_Awards4Points()
        {
            // Match ended 1:1, user predicted 1:1 and selected Away to advance (2). Home won.
            var points = _scoring.CalculatePoints(1, 1, "HOME_TEAM", 1, 1, 2);
            Assert.Equal(4, points); // 4 for exact draw
        }

        [Fact]
        public void CalculatePoints_CorrectWinner_WrongScore_Awards2Points()
        {
            // User predicted 3:0, match ended 2:1 (Home won)
            var points = _scoring.CalculatePoints(2, 1, "HOME_TEAM", 3, 0, null);
            Assert.Equal(2, points);

            // User predicted 0:2, match ended 1:3 (Away won)
            var awayPoints = _scoring.CalculatePoints(1, 3, "AWAY_TEAM", 0, 2, null);
            Assert.Equal(2, awayPoints);
        }

        [Fact]
        public void CalculatePoints_DrawPredicted_DrawEnded_Awards2Points()
        {
            // User predicted 0:0, match ended 2:2 without winner info
            var points = _scoring.CalculatePoints(2, 2, null, 0, 0, null);
            Assert.Equal(2, points);
        }

        [Fact]
        public void CalculatePoints_DrawPredicted_DrawEnded_WithCorrectPlayoffWinner_Awards3Points()
        {
            // User predicted 0:0, match ended 1:1, user predicted Home (1) to advance, Home won
            var points = _scoring.CalculatePoints(1, 1, "HOME_TEAM", 0, 0, 1);
            Assert.Equal(3, points); // 2 for draw + 1 for correct playoff winner
        }

        [Fact]
        public void CalculatePoints_PredictedWin_StandardDraw_TeamWonInOvertime_Awards1Point()
        {
            // User predicted Home win 2:1. Standard time was 1:1 (Draw), but Home won in overtime.
            var points = _scoring.CalculatePoints(1, 1, "HOME_TEAM", 2, 1, null);
            Assert.Equal(1, points);
        }

        [Fact]
        public void CalculatePoints_PredictedDrawWithPick_TeamWonInStandardTime_Awards1Point()
        {
            // User predicted 1:1 with Home advancing (1). Match ended 2:1 in standard time.
            var points = _scoring.CalculatePoints(2, 1, "HOME_TEAM", 1, 1, 1);
            Assert.Equal(1, points);
        }

        [Fact]
        public void CalculatePoints_WrongWinner_Awards0Points()
        {
            // User predicted 2:0, match ended 0:1
            var points = _scoring.CalculatePoints(0, 1, "AWAY_TEAM", 2, 0, null);
            Assert.Equal(0, points);

            // User predicted Away win, Home won in standard time
            var points2 = _scoring.CalculatePoints(3, 1, "HOME_TEAM", 0, 2, null);
            Assert.Equal(0, points2);
        }
    }
}
