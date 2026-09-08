using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Discord.Models
{
    /// <summary>
    /// Represents a participating Discord user and their accumulated contest points.
    /// </summary>
    public class User
    {
        [Key]
        public ulong DiscordId { get; set; }
        public string Username { get; set; } = string.Empty;
        public int Points { get; set; }
        
        public virtual ICollection<Bet> Bets { get; set; } = new List<Bet>();
    }

    /// <summary>
    /// Represents a football match synchronized from football-data.org.
    /// </summary>
    public class Match
    {
        [Key]
        public int Id { get; set; } // External API match identifier
        public string HomeTeam { get; set; } = string.Empty;
        public string AwayTeam { get; set; } = string.Empty;
        public string HomeTeamFlag { get; set; } = string.Empty;
        public string AwayTeamFlag { get; set; } = string.Empty;
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public string? Winner { get; set; } // "HOME_TEAM", "AWAY_TEAM", "DRAW"
        public int? HomeStandardScore { get; set; }
        public int? AwayStandardScore { get; set; }
        public DateTime KickOffTime { get; set; }
        public string Status { get; set; } = string.Empty; // TIMED, IN_PLAY, FINISHED, etc.
        
        [NotMapped]
        public string? Duration { get; set; }
        
        public virtual ICollection<Bet> Bets { get; set; } = new List<Bet>();
    }

    /// <summary>
    /// Represents a bet placed by a user on a specific match.
    /// </summary>
    public class Bet
    {
        public int Id { get; set; }
        public int MatchId { get; set; }
        public virtual Match Match { get; set; } = null!;
        public ulong UserId { get; set; }
        public virtual User User { get; set; } = null!;
        
        public int PredictedHomeScore { get; set; }
        public int PredictedAwayScore { get; set; }
        public int? PredictedWinner { get; set; } // 1 = Home, 2 = Away, null = Not Specified
        public int? PointsAwarded { get; set; }
        public bool IsProcessed { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Key-value persistent state entity for storing runtime metadata (e.g. Discord message IDs).
    /// </summary>
    public class BotSetting
    {
        [Key]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
