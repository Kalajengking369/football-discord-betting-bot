using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Discord.Models;

namespace Discord.Services
{
    public class FootballApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FootballApiService> _logger;
        private readonly string _apiKey;

        public FootballApiService(
            HttpClient httpClient, 
            IConfiguration configuration,
            ILogger<FootballApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["FootballApiKey"] ?? throw new ArgumentNullException("FootballApiKey is missing in configuration");
            _httpClient.BaseAddress = new Uri("https://api.football-data.org/v4/");
            _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", _apiKey);
        }

        /// <summary>
        /// Retrieves upcoming and active matches for the specified competition from football-data.org.
        /// </summary>
        /// <param name="competitionId">The football-data.org competition identifier (e.g. 2000 for World Cup)</param>
        /// <returns>List of mapped Match entities</returns>
        public async Task<List<Match>> GetUpcomingMatchesAsync(int competitionId)
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<FootballDataResponse>($"competitions/{competitionId}/matches");
                
                if (response?.Matches == null) return new List<Match>();

                return response.Matches
                    .Where(m => m.HomeTeam?.Name != null && m.AwayTeam?.Name != null)
                    .Select(m => new Match
                    {
                        Id = m.Id,
                        HomeTeam = m.HomeTeam.Name,
                        AwayTeam = m.AwayTeam.Name,
                        HomeTeamFlag = m.HomeTeam.Crest ?? "",
                        AwayTeamFlag = m.AwayTeam.Crest ?? "",
                        HomeScore = m.Score?.FullTime?.Home,
                        AwayScore = m.Score?.FullTime?.Away,
                        Winner = m.Score?.Winner,
                        HomeStandardScore = m.Score?.RegularTime?.Home ?? m.Score?.FullTime?.Home,
                        AwayStandardScore = m.Score?.RegularTime?.Away ?? m.Score?.FullTime?.Away,
                        KickOffTime = m.UtcDate,
                        Status = m.Status,
                        Duration = m.Score?.Duration
                    }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve matches for competition {CompetitionId}", competitionId);
                return new List<Match>();
            }
        }

        /// <summary>
        /// Fetches detailed match information (including extra time and penalty details) for a specific match.
        /// </summary>
        /// <param name="matchId">The match identifier</param>
        /// <returns>Mapped Match entity or null if request fails</returns>
        public async Task<Match?> GetMatchByIdAsync(int matchId)
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<ApiMatch>($"matches/{matchId}");
                if (response == null) return null;

                return new Match
                {
                    Id = response.Id,
                    HomeTeam = response.HomeTeam.Name,
                    AwayTeam = response.AwayTeam.Name,
                    HomeTeamFlag = response.HomeTeam.Crest ?? "",
                    AwayTeamFlag = response.AwayTeam.Crest ?? "",
                    HomeScore = response.Score?.FullTime?.Home,
                    AwayScore = response.Score?.FullTime?.Away,
                    Winner = response.Score?.Winner,
                    HomeStandardScore = response.Score?.RegularTime?.Home ?? response.Score?.FullTime?.Home,
                    AwayStandardScore = response.Score?.RegularTime?.Away ?? response.Score?.FullTime?.Away,
                    KickOffTime = response.UtcDate,
                    Status = response.Status,
                    Duration = response.Score?.Duration
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch match details for ID {MatchId}", matchId);
                return null;
            }
        }
    }

    public class FootballDataResponse
    {
        [JsonPropertyName("matches")]
        public List<ApiMatch> Matches { get; set; } = new();
    }

    public class ApiMatch
    {
        public int Id { get; set; }
        public DateTime UtcDate { get; set; }
        public string Status { get; set; } = "";
        public ApiTeam HomeTeam { get; set; } = null!;
        public ApiTeam AwayTeam { get; set; } = null!;
        public ApiScore Score { get; set; } = null!;
    }

    public class ApiTeam
    {
        public string Name { get; set; } = "";
        [JsonPropertyName("crest")]
        public string? Crest { get; set; }
    }

    public class ApiScore
    {
        [JsonPropertyName("winner")]
        public string? Winner { get; set; }
        [JsonPropertyName("duration")]
        public string? Duration { get; set; }
        [JsonPropertyName("fullTime")]
        public ApiFullTime FullTime { get; set; } = null!;
        [JsonPropertyName("regularTime")]
        public ApiFullTime? RegularTime { get; set; }
        [JsonPropertyName("extraTime")]
        public ApiFullTime? ExtraTime { get; set; }
        [JsonPropertyName("penalties")]
        public ApiFullTime? Penalties { get; set; }
    }

    public class ApiFullTime
    {
        [JsonPropertyName("home")]
        public int? Home { get; set; }
        [JsonPropertyName("away")]
        public int? Away { get; set; }
    }
}
