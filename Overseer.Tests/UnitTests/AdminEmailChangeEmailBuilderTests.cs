using Overseer.Services.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>The instructions email an administrator's email change sends: its link, wording and encoding.</summary>
public sealed class AdminEmailChangeEmailBuilderTests
{
    private const string Url = "https://account.example.com/Identity/Account/ResendEmailConfirmation";

    [Theory]
    [InlineData("https://account.example.com")]
    [InlineData("https://account.example.com/")]
    public void TheResendConfirmationUrlJoinsTheBaseWithOrWithoutATrailingSlash(string baseUrl)
    {
        Assert.Equal(Url, AdminEmailChangeEmailBuilder.ResendConfirmationUrl(baseUrl));
    }

    [Fact]
    public void TheSubjectNamesTheNewAddressConfirmation()
    {
        var (subject, _) = AdminEmailChangeEmailBuilder.Build("gnoll", "new@example.com", Url);

        Assert.Equal("Confirm the new email address of your GnollHack account", subject);
    }

    [Fact]
    public void TheBodyCarriesTheNameTheAddressTheLinkAndWhy()
    {
        var (_, html) = AdminEmailChangeEmailBuilder.Build("gnoll", "new@example.com", Url);

        Assert.Contains("Hello gnoll,", html);
        Assert.Contains("new@example.com", html);
        Assert.Contains($"<a href=\"{Url}\">{Url}</a>", html);
        Assert.Contains("Why this extra step", html);
        Assert.Contains("Your user name stays gnoll", html);
    }

    [Fact]
    public void AUserNameWithMarkupIsHtmlEncoded()
    {
        var (_, html) = AdminEmailChangeEmailBuilder.Build("<b>x</b>&", "new@example.com", Url);

        Assert.DoesNotContain("<b>x</b>&", html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;&amp;", html);
    }
}
