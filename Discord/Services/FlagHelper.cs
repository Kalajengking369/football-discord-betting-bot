using System.Collections.Generic;

namespace Discord.Services
{
    /// <summary>
    /// Provides helper methods for mapping international team names to flag emojis and standardized display names.
    /// </summary>
    public static class FlagHelper
    {
        private static readonly Dictionary<string, (string Flag, string Name)> _teams = new(StringComparer.OrdinalIgnoreCase)
        {
            {"Algeria", ("🇩🇿", "Algeria")},
            {"Argentina", ("🇦🇷", "Argentina")},
            {"Australia", ("🇦🇺", "Australia")},
            {"Austria", ("🇦🇹", "Austria")},
            {"Belgium", ("🇧🇪", "Belgium")},
            {"Bosnia-Herzegovina", ("🇧🇦", "Bosnia and Herzegovina")},
            {"Brazil", ("🇧🇷", "Brazil")},
            {"Canada", ("🇨🇦", "Canada")},
            {"Cape Verde Islands", ("🇨🇻", "Cape Verde")},
            {"Colombia", ("🇨🇴", "Colombia")},
            {"Congo DR", ("🇨🇩", "DR Congo")},
            {"Croatia", ("🇭🇷", "Croatia")},
            {"Curaçao", ("🇨🇼", "Curaçao")},
            {"Czechia", ("🇨🇿", "Czechia")},
            {"Czech Republic", ("🇨🇿", "Czechia")},
            {"Ecuador", ("🇪🇨", "Ecuador")},
            {"Egypt", ("🇪🇬", "Egypt")},
            {"England", ("🏴󠁧󠁢󠁥󠁮󠁧󠁿", "England")},
            {"France", ("🇫🇷", "France")},
            {"Germany", ("🇩🇪", "Germany")},
            {"Ghana", ("🇬🇭", "Ghana")},
            {"Haiti", ("🇭🇹", "Haiti")},
            {"Hungary", ("🇭🇺", "Hungary")},
            {"Chile", ("🇨🇱", "Chile")},
            {"Iran", ("🇮🇷", "Iran")},
            {"Iraq", ("🇮🇶", "Iraq")},
            {"Ivory Coast", ("🇨🇮", "Ivory Coast")},
            {"Jamaica", ("🇯🇲", "Jamaica")},
            {"Japan", ("🇯🇵", "Japan")},
            {"Jordan", ("🇯🇴", "Jordan")},
            {"Mexico", ("🇲🇽", "Mexico")},
            {"Morocco", ("🇲🇦", "Morocco")},
            {"Netherlands", ("🇳🇱", "Netherlands")},
            {"New Zealand", ("🇳🇿", "New Zealand")},
            {"Norway", ("🇳🇴", "Norway")},
            {"Panama", ("🇵🇦", "Panama")},
            {"Paraguay", ("🇵🇾", "Paraguay")},
            {"Peru", ("🇵🇪", "Peru")},
            {"Poland", ("🇵🇱", "Poland")},
            {"Portugal", ("🇵🇹", "Portugal")},
            {"Qatar", ("🇶🇦", "Qatar")},
            {"Romania", ("🇷🇴", "Romania")},
            {"Saudi Arabia", ("🇸🇦", "Saudi Arabia")},
            {"Scotland", ("🏴󠁧󠁢󠁳󠁣󠁴󠁿", "Scotland")},
            {"Senegal", ("🇸🇳", "Senegal")},
            {"Serbia", ("🇷🇸", "Serbia")},
            {"Slovakia", ("🇸🇰", "Slovakia")},
            {"Slovenia", ("🇸🇮", "Slovenia")},
            {"South Africa", ("🇿🇦", "South Africa")},
            {"South Korea", ("🇰🇷", "South Korea")},
            {"Spain", ("🇪🇸", "Spain")},
            {"Sweden", ("🇸🇪", "Sweden")},
            {"Switzerland", ("🇨🇭", "Switzerland")},
            {"Tunisia", ("🇹🇳", "Tunisia")},
            {"Turkey", ("🇹🇷", "Turkey")},
            {"Ukraine", ("🇺🇦", "Ukraine")},
            {"United States", ("🇺🇸", "United States")},
            {"Uruguay", ("🇺🇾", "Uruguay")},
            {"Uzbekistan", ("🇺🇿", "Uzbekistan")},
            {"Venezuela", ("🇻🇪", "Venezuela")},
            {"Wales", ("🏴󠁧󠁢󠁷󠁬󠁳󠁿", "Wales")}
        };

        /// <summary>
        /// Returns the flag emoji corresponding to the given team name.
        /// </summary>
        public static string GetFlag(string teamName) => _teams.TryGetValue(teamName, out var data) ? data.Flag : "🏳️";

        /// <summary>
        /// Returns the standardized display name for the given team name.
        /// </summary>
        public static string GetName(string teamName) => _teams.TryGetValue(teamName, out var data) ? data.Name : teamName;
    }
}
