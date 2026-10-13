namespace MobileGnollHackLogger.Areas.API
{
    /// <summary>Names of the website's rate-limiting policies, registered in Program.cs.</summary>
    public static class GameApiRateLimits
    {
        /// <summary>Per remote IP on the game APIs' POST actions; far above one client's worst retry loop.</summary>
        public const string GameApiFlood = "GameApiFlood";

        /// <summary>Per remote IP on the Razor account pages that sign in or send email.</summary>
        public const string AccountPages = "AccountPages";
    }
}
