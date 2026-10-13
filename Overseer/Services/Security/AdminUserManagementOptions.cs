namespace Overseer.Services.Security;

/// <summary>The <c>AdminUserManagement</c> section: limits of the per-user admin actions and the Bulk Actions wizard.</summary>
public class AdminUserManagementOptions
{
    /// <summary>The most accounts one bulk operation takes, oldest first.</summary>
    public int BulkMaxUsers { get; set; } = 500;

    /// <summary>Bulk delete refuses an account registered within this many days.</summary>
    public int BulkDeleteMinAccountAgeDays { get; set; } = 7;

    /// <summary>Bulk delete refuses an account that signed in within this many days.</summary>
    public int BulkDeleteRecentSignInDays { get; set; } = 90;

    /// <summary>How long a bulk preview can be executed.</summary>
    public int BulkPreviewMinutes { get; set; } = 15;

    /// <summary>The furthest ahead an administrator can set a lockout's end.</summary>
    public int LockoutMaxDays { get; set; } = 365;

    /// <summary>A bulk run stops after this many failed accounts in a row.</summary>
    public int BulkConsecutiveFailureLimit { get; set; } = 5;
}
