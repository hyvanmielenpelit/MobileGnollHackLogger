namespace MobileGnollHackLogger.Filters;

/// <summary>
/// Exempts a Razor page model from <see cref="RequireAccountTermsPageFilter"/>, so a signed-in user
/// who has not accepted the current account Terms can still reach it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowWithoutAccountTermsAttribute : Attribute
{
}
