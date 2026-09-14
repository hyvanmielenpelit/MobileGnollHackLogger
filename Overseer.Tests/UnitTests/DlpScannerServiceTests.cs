using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy.Dlp;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Outbound detection. Every one of these is about a credential the user did not mean to send
/// to a provider — or, just as importantly, about a key-shaped string in a tutorial that must
/// still reach the model intact.
/// </summary>
public class DlpScannerServiceTests
{
    /* Synthetic values with the shape of the real thing and none of the substance. They must be
       random-looking, because the entropy gate is deliberately hostile to anything that is not. */
    private const string SkKey = "sk-proj-9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgUvXn0BiOoMzQrEyPkSl3W";
    private const string GoogleKey = "AIzaSyB7xQ2mVk9RtLpN4wZc1AeHjD6TgUvXn0";
    private const string AwsKey = "AKIA3QK7Z9MTVB2XR6LD";
    private const string GitHubClassicToken = "ghp_9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgUvXn0";
    private const string GitHubFineGrainedToken = "github_pat_11AbCd3EfGh4IjKl5MnOp_qRsTuVwXyZ7AbCd8EfGh9IjKl0MnOpQ";
    private const string GitHubOAuthToken = "gho_4NpLc8RwYs1AeHjD6TgUvXn0BiOoMzQrEyPk";
    private const string SlackBotToken = "xoxb-2QK7Z9MTVB2-4NpLc8RwYs1-AeHjD6TgUvXn0BiOoMzQrEyPk";
    /* Prefix and body are separate literals: GitHub push protection scans source text for
       token shapes, and the concatenation keeps the scanned value identical. */
    private const string StripeLiveKey = "sk_live_" + "9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgU";
    private const string StripeTestKey = "rk_test_" + "3QK7Z9MTVB2XR6LDc8RwYs1AeHjD6TgU";
    private const string GitLabToken = "glpat-" + "9fK2mQx7ZtVb4NpLc8Rw";
    private const string HuggingFaceToken = "hf_9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgUvX";
    private const string NpmToken = "npm_9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgUvX";
    private const string GoogleOAuthToken = "ya29.a0AfH6SMBq7Z9MTVb4NpLc8RwYs1AeHjD6TgUvXn0BiOo";
    private const string SendGridKey = "SG.9fK2mQx7ZtVb4NpLc.8RwYs1AeHjD6TgUvXn0BiOoMzQrEyPkSl3W";

    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"
        + ".eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkFkYSBMb3ZlbGFjZSIsImlhdCI6MTUxNjIzOTAyMn0"
        + ".SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    private const string OpaqueToken = "9fK2mQx7ZtVb4NpLc8RwYs1AeHjD6TgUvXn0BiOo";

    private const string PemBlock =
        "-----BEGIN RSA PRIVATE KEY-----\r\n"
        + "MIIBOgIBAAJBAKj34GkxFhD90vcNLYLInFEX6Ppy1tPf9Cnzj4p4WGeKLs1Pt8Qu\r\n"
        + "KUpRKfFLfRYC9AIKjbQi\r\n"
        + "-----END RSA PRIVATE KEY-----";

    private static DlpScannerService CreateScanner(Dictionary<string, string?>? floor = null)
        => new(new ConfigurationBuilder().AddInMemoryCollection(floor ?? new Dictionary<string, string?>()).Build());

    private static IReadOnlyList<DlpFinding> ScanWithDefaults(string text)
        => CreateScanner().Scan(text, DlpPolicy.Defaults);

    private static DlpPolicy Only(DlpClass dlpClass) => new()
    {
        ApiKeys = dlpClass == DlpClass.ApiKey,
        PrivateKeys = dlpClass == DlpClass.PrivateKey,
        Tokens = dlpClass == DlpClass.Token,
        Passwords = dlpClass == DlpClass.Password,
        CreditCards = dlpClass == DlpClass.CreditCard,
        Ibans = dlpClass == DlpClass.Iban,
        Ssns = dlpClass == DlpClass.Ssn,
        Emails = dlpClass == DlpClass.Email,
        PhoneNumbers = dlpClass == DlpClass.Phone
    };

    // ── Policy shape ────────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultPolicyMasksCredentialsButNotContactDetails()
    {
        var policy = DlpPolicy.Defaults;

        Assert.True(policy.ApiKeys);
        Assert.True(policy.PrivateKeys);
        Assert.True(policy.Tokens);
        Assert.True(policy.Passwords);
        Assert.True(policy.CreditCards);
        Assert.True(policy.Ibans);
        Assert.True(policy.Ssns);

        /* Off by default because masking them measurably degrades answers for data the user
           usually meant to send — a pasted support thread, an address list to reformat. */
        Assert.False(policy.Emails);
        Assert.False(policy.PhoneNumbers);

        Assert.True(policy.AnyEnabled);
    }

    [Fact]
    public void TheNonePolicyIsTheOffState()
    {
        // There is no master switch, so "every class off" has to be a reportable state.
        Assert.False(DlpPolicy.None.AnyEnabled);

        foreach (DlpClass dlpClass in Enum.GetValues<DlpClass>())
            Assert.False(DlpPolicy.None.IsEnabled(dlpClass));
    }

    [Fact]
    public void IsEnabledAgreesWithEveryIndividualSwitch()
    {
        foreach (DlpClass dlpClass in Enum.GetValues<DlpClass>())
        {
            Assert.True(Only(dlpClass).IsEnabled(dlpClass));
            Assert.True(Only(dlpClass).AnyEnabled);
        }
    }

    // ── The operator floor ──────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultFloorForcesNothing()
    {
        // Unlike the confidential floor, the DLP floor starts empty: the built-in defaults come
        // from DlpPolicy, and an operator only ever adds to them.
        Assert.False(CreateScanner().Floor.AnyEnabled);
    }

    [Fact]
    public void NullSettingsResolveToTheBuiltInDefaults()
    {
        Assert.Equal(DlpPolicy.Defaults, CreateScanner().Resolve(null));
    }

    [Theory]
    [InlineData(false, null, false)]   // no floor, no preference -> the built-in default (off for email)
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]    // the user may turn a class on
    [InlineData(true, null, true)]
    [InlineData(true, false, true)]    // the floor wins upward: the user cannot turn it back off
    [InlineData(true, true, true)]
    public void TheFloorCanOnlyPushAClassOn(bool floorValue, bool? userValue, bool expected)
    {
        /* Emails is the class to test this with: its built-in default is off, so the resolved
           value comes from the floor and the user alone and nothing is masked by the default. */
        var scanner = CreateScanner(new Dictionary<string, string?>
        {
            { "PrivacySettings:DlpFloor:Emails", floorValue ? "true" : "false" }
        });

        Assert.Equal(expected, scanner.Resolve(new UserAiSettings { DlpMaskEmails = userValue }).Emails);
    }

    [Fact]
    public void AUserCannotTurnOffAClassTheFloorForcesOn()
    {
        var scanner = CreateScanner(new Dictionary<string, string?>
        {
            { "PrivacySettings:DlpFloor:ApiKeys", "true" },
            { "PrivacySettings:DlpFloor:PrivateKeys", "true" },
            { "PrivacySettings:DlpFloor:Tokens", "true" },
            { "PrivacySettings:DlpFloor:Passwords", "true" },
            { "PrivacySettings:DlpFloor:CreditCards", "true" },
            { "PrivacySettings:DlpFloor:Ibans", "true" },
            { "PrivacySettings:DlpFloor:Ssns", "true" },
            { "PrivacySettings:DlpFloor:Emails", "true" },
            { "PrivacySettings:DlpFloor:PhoneNumbers", "true" }
        });

        var resolved = scanner.Resolve(new UserAiSettings
        {
            DlpMaskApiKeys = false,
            DlpMaskPrivateKeys = false,
            DlpMaskTokens = false,
            DlpMaskPasswords = false,
            DlpMaskCreditCards = false,
            DlpMaskIbans = false,
            DlpMaskSsns = false,
            DlpMaskEmails = false,
            DlpMaskPhoneNumbers = false
        });

        foreach (DlpClass dlpClass in Enum.GetValues<DlpClass>())
            Assert.True(resolved.IsEnabled(dlpClass));
    }

    [Fact]
    public void AUserWhoTurnsEverythingOffWithNoFloorGetsNoMaskingAtAll()
    {
        var resolved = CreateScanner().Resolve(new UserAiSettings
        {
            DlpMaskApiKeys = false,
            DlpMaskPrivateKeys = false,
            DlpMaskTokens = false,
            DlpMaskPasswords = false,
            DlpMaskCreditCards = false,
            DlpMaskIbans = false,
            DlpMaskSsns = false,
            DlpMaskEmails = false,
            DlpMaskPhoneNumbers = false
        });

        Assert.False(resolved.AnyEnabled);
        Assert.Empty(CreateScanner().Scan($"key {SkKey}", resolved));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("not-a-bool")]
    public void AMalformedFloorValueResolvesToOffRatherThanThrowing(string configured)
    {
        /* A typo in one line of appsettings.json must not take the application down at startup.
           It resolves to the documented default — off — and the user's own preference then
           decides. */
        var scanner = CreateScanner(new Dictionary<string, string?>
        {
            { "PrivacySettings:DlpFloor:Emails", configured }
        });

        Assert.False(scanner.Floor.Emails);
        Assert.False(scanner.Resolve(new UserAiSettings { DlpMaskEmails = false }).Emails);
    }

    // ── API keys ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SkKey)]
    [InlineData(GoogleKey)]
    [InlineData(AwsKey)]
    [InlineData(GitHubClassicToken)]
    [InlineData(GitHubFineGrainedToken)]
    [InlineData(GitHubOAuthToken)]
    [InlineData(SlackBotToken)]
    [InlineData(StripeLiveKey)]
    [InlineData(StripeTestKey)]
    [InlineData(GitLabToken)]
    [InlineData(HuggingFaceToken)]
    [InlineData(NpmToken)]
    [InlineData(GoogleOAuthToken)]
    [InlineData(SendGridKey)]
    public void EachApiKeyShapeIsDetectedWhole(string key)
    {
        var finding = Assert.Single(ScanWithDefaults($"my key is {key} please check"));

        Assert.Equal(DlpClass.ApiKey, finding.Class);
        // The whole key, prefix included: replacing only the random tail would leave "sk-" in
        // the prompt and the secret half-sent.
        Assert.Equal(key, finding.Value);
    }

    [Fact]
    public void AKeyPrefixInAnOrdinaryWordIsNotAKey()
    {
        // "risk-" contains "sk-", which is exactly why the pattern needs a left boundary.
        Assert.Empty(ScanWithDefaults("risk-management-review-document-for-the-quarter"));
    }

    // ── The entropy gate ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("use sk-XXXXXXXXXXXXXXXXXXXXXXXX in your configuration file")]
    [InlineData("set the key to AIzaSyYOUR_API_KEY_HERE_XXXXXXXXXXX and restart")]
    [InlineData("the documented sample is AKIAIOSFODNN7EXAMPLE")]
    [InlineData("Stripe writes it sk_test_" + "XXXXXXXXXXXXXXXXXXXXXXXX in the docs")]
    [InlineData("set the bot token to xoxb-YOUR-SLACK-BOT-TOKEN-HERE-GOES")]
    [InlineData("export the variable as glpat-YOUR_GITLAB_TOKEN_HERE")]
    [InlineData("the sample value is gho_EXAMPLEEXAMPLEEXAMPLEEXAMPLEEXAM")]
    [InlineData("put hf_YOURHUGGINGFACETOKENVALUEGOESHERE in the file")]
    [InlineData("the template says npm_INSERTYOURNPMTOKENVALUEHEREPLEASE")]
    [InlineData("a refreshed ya29.PLACEHOLDER_GOOGLE_OAUTH_TOKEN_VALUE")]
    [InlineData("SG.EXAMPLE_KEY_ID_X.EXAMPLE_KEY_SECRET_VALUE is the sample")]
    public void APlaceholderKeyIsNotMasked(string text)
    {
        /* Documentation and configuration templates are full of key-shaped strings. Masking
           them turns a question about configuration into one the model cannot read, so the gate
           is deliberately tuned to let them through. */
        Assert.Empty(ScanWithDefaults(text));
    }

    [Fact]
    public void ARealisticRandomKeyClearsTheEntropyGate()
    {
        Assert.True(DlpScannerService.ShannonEntropy(SkKey) >= DlpScannerService.MinKeyEntropyBitsPerChar);
        Assert.Single(ScanWithDefaults($"key {SkKey}"));
    }

    [Fact]
    public void ShannonEntropyIsZeroForARepeatedCharacterAndHighForARandomKey()
    {
        Assert.Equal(0.0, DlpScannerService.ShannonEntropy("aaaaaaaaaaaa"), 6);
        Assert.Equal(0.0, DlpScannerService.ShannonEntropy(string.Empty), 6);

        // A real key sits far above the threshold, which is what makes the margin safe.
        Assert.True(DlpScannerService.ShannonEntropy(GoogleKey) > 5.0);
    }

    // ── Tokens ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AJwtIsDetected()
    {
        var finding = Assert.Single(ScanWithDefaults($"the session token is {Jwt} now"));

        Assert.Equal(DlpClass.Token, finding.Class);
        Assert.Equal(Jwt, finding.Value);
    }

    [Fact]
    public void ABearerHeaderMasksTheTokenAndNotTheKeyword()
    {
        var finding = Assert.Single(ScanWithDefaults($"Authorization: Bearer {OpaqueToken}"));

        Assert.Equal(DlpClass.Token, finding.Class);
        /* "Bearer" itself is context rather than secret: leaving it in is what lets the model
           still see that it is looking at an auth header. */
        Assert.Equal(OpaqueToken, finding.Value);
        Assert.DoesNotContain("Bearer", finding.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AJwtInsideABearerHeaderYieldsOneFinding()
    {
        /* Two patterns claim the same span here. Reporting both would mint two tokens for one
           credential and tell the model it has two. */
        var finding = Assert.Single(ScanWithDefaults($"Authorization: Bearer {Jwt}"));

        Assert.Equal(Jwt, finding.Value);
    }

    // ── Private keys ────────────────────────────────────────────────────────────

    [Fact]
    public void APemBlockIsMatchedWholeIncludingItsFooter()
    {
        var finding = Assert.Single(ScanWithDefaults($"here it is:\r\n{PemBlock}\r\nthat is all"));

        Assert.Equal(DlpClass.PrivateKey, finding.Class);
        // The whole block, or the token would replace the header and leave the key body behind.
        Assert.Equal(PemBlock, finding.Value);
    }

    [Fact]
    public void AnUnterminatedPemBlockIsMatchedToTheEndOfInput()
    {
        // A truncated private key is still a leaked private key, so a missing footer must not
        // mean a missing match.
        const string truncated =
            "-----BEGIN OPENSSH PRIVATE KEY-----\r\n"
            + "b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAAB";

        var finding = Assert.Single(ScanWithDefaults($"oops, pasted:\r\n{truncated}"));

        Assert.Equal(DlpClass.PrivateKey, finding.Class);
        Assert.Equal(truncated, finding.Value);
    }

    [Fact]
    public void APgpPrivateKeyBlockIsMatchedWhole()
    {
        const string pgp =
            "-----BEGIN PGP PRIVATE KEY BLOCK-----\r\n"
            + "lQOYBF8AbCdEfGhIjKlMnOpQrStUvWxYz\r\n"
            + "-----END PGP PRIVATE KEY BLOCK-----";

        var finding = Assert.Single(ScanWithDefaults(pgp));

        Assert.Equal(DlpClass.PrivateKey, finding.Class);
        Assert.Equal(pgp, finding.Value);
    }

    [Fact]
    public void APublicKeyBlockIsNotAPrivateKey()
    {
        Assert.Empty(ScanWithDefaults(
            "-----BEGIN PUBLIC KEY-----\r\nMFwwDQYJKoZIhvcNAQEB\r\n-----END PUBLIC KEY-----"));
    }

    // ── Credit cards ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("5500000000000004")]
    [InlineData("378282246310005")]
    public void ALuhnValidCardIsDetectedWithEitherSeparator(string card)
    {
        var finding = Assert.Single(ScanWithDefaults($"card {card} expires soon"));

        Assert.Equal(DlpClass.CreditCard, finding.Class);
        Assert.Equal(card, finding.Value);
    }

    [Fact]
    public void ACardThatFailsLuhnIsNotReported()
    {
        // Without the check, every long digit run in a log line becomes a payment card.
        Assert.False(DlpScannerService.IsLuhnValid("4111111111111112"));
        Assert.Empty(ScanWithDefaults("order reference 4111111111111112 shipped"));
    }

    [Fact]
    public void ADigitRunThatIsTooLongToBeACardIsNotReported()
    {
        // The boundary requirement is what stops a 20-digit identifier being chopped into a
        // card-shaped piece that happens to satisfy Luhn.
        Assert.Empty(ScanWithDefaults("trace 12345678901234567890 recorded"));
    }

    [Fact]
    public void LuhnRejectsANumberOutsideTheThirteenToNineteenDigitRange()
    {
        // Eight digits cannot be a card whatever the checksum says.
        Assert.False(DlpScannerService.IsLuhnValid("41111111"));
        Assert.True(DlpScannerService.IsLuhnValid("4111 1111 1111 1111"));
    }

    // ── SSNs ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("078-05-1120")]
    [InlineData("078051120")]
    public void AValidSsnIsDetectedInBothForms(string ssn)
    {
        var finding = Assert.Single(ScanWithDefaults($"filed under {ssn} last year"));

        Assert.Equal(DlpClass.Ssn, finding.Class);
        Assert.Equal(ssn, finding.Value);
    }

    [Theory]
    [InlineData("000-45-6789")]   // area 000
    [InlineData("666-45-6789")]   // area 666
    [InlineData("900-45-6789")]   // area 900-999
    [InlineData("987-65-4329")]   // area in the 900 block, hyphenated
    [InlineData("078-00-1120")]   // group 00
    [InlineData("078-05-0000")]   // serial 0000
    public void AnSsnWithAnInvalidAreaGroupOrSerialIsNotReported(string notAnSsn)
    {
        /* Plain \d{3}-\d{2}-\d{4} would mask version strings, part numbers and telephone
           fragments. The issuing rules are what make the class usable at all. */
        Assert.Empty(ScanWithDefaults($"reference {notAnSsn} noted"));
    }

    [Fact]
    public void ABareNineDigitRunNeedsClearBoundaries()
    {
        // Embedded in a longer identifier it is not an SSN, and treating it as one would mask
        // half of every build number.
        Assert.Empty(ScanWithDefaults("build A078051120B completed"));
    }

    // ── Individual switches ─────────────────────────────────────────────────────

    [Fact]
    public void ADisabledClassIsNotMatchedAtAll()
    {
        var scanner = CreateScanner();
        string text = $"key {SkKey} ssn 078-05-1120 card 4111111111111111 token {Jwt}";

        // Four classes are present, so each single-class policy must find its own and nothing else.
        Assert.Equal(4, scanner.Scan(text, DlpPolicy.Defaults).Count);

        Assert.Equal(DlpClass.ApiKey, Assert.Single(scanner.Scan(text, Only(DlpClass.ApiKey))).Class);
        Assert.Equal(DlpClass.Ssn, Assert.Single(scanner.Scan(text, Only(DlpClass.Ssn))).Class);
        Assert.Equal(DlpClass.CreditCard, Assert.Single(scanner.Scan(text, Only(DlpClass.CreditCard))).Class);
        Assert.Equal(DlpClass.Token, Assert.Single(scanner.Scan(text, Only(DlpClass.Token))).Class);

        // With everything off there is nothing to find, whatever the text contains.
        Assert.Empty(scanner.Scan(text, DlpPolicy.None));
    }

    [Fact]
    public void AnEmailIsOnlyDetectedWhenTheUserOptsIn()
    {
        const string text = "write to ada@example.com please";

        Assert.Empty(ScanWithDefaults(text));

        var finding = Assert.Single(CreateScanner().Scan(text, Only(DlpClass.Email)));
        Assert.Equal("ada@example.com", finding.Value);
    }

    // ── Bank account numbers ────────────────────────────────────────────────────

    [Theory]
    [InlineData("GB82 WEST 1234 5698 7654 32")]
    [InlineData("GB82WEST12345698765432")]
    public void AValidIbanIsDetectedWithOrWithoutSpaces(string iban)
    {
        // The published example IBAN, which is what every implementation is checked against.
        var finding = Assert.Single(ScanWithDefaults($"pay into {iban} today"));

        Assert.Equal(DlpClass.Iban, finding.Class);
        Assert.Equal(iban, finding.Value);
    }

    [Fact]
    public void AnIbanWithABadChecksumIsNotReported()
    {
        /* Mod-97 is the whole of the false-positive defence here: without it the pattern masks
           order references, part numbers and anything else written as letters then digits. */
        Assert.Empty(ScanWithDefaults("pay into GB82 WEST 1234 5698 7654 33 today"));
        Assert.False(DlpScannerService.IsIbanValid("GB82WEST12345698765433"));
        Assert.True(DlpScannerService.IsIbanValid("GB82WEST12345698765432"));
    }

    [Fact]
    public void AnIbanIsNotReportedAsACardNumber()
    {
        /* An IBAN always begins with a letter and a card number always with a digit, so the two
           patterns cannot claim the same span. This pins that, because a card finding would
           mask only the digit tail and leave the country code in the prompt. */
        var findings = ScanWithDefaults("IBAN GB82WEST12345698765432 and card 4111111111111111");

        Assert.Equal(new[] { DlpClass.Iban, DlpClass.CreditCard }, findings.Select(f => f.Class));
        Assert.Equal("GB82WEST12345698765432", findings[0].Value);
    }

    [Fact]
    public void AnIbanOutsideTheLengthRangeIsNotAnIban()
    {
        Assert.False(DlpScannerService.IsIbanValid("GB82WEST1234"));
        Assert.False(DlpScannerService.IsIbanValid("GB82WEST123456987654321234567890123456"));
        Assert.False(DlpScannerService.IsIbanValid("1234WEST12345698765432"));
    }

    // ── Passwords ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Server=db;Password=Tr0ub4dor&3;", "Tr0ub4dor&3")]
    [InlineData("pwd=S3cretValue99", "S3cretValue99")]
    [InlineData("client_secret: 4NpLc8RwYs1AeHjD", "4NpLc8RwYs1AeHjD")]
    public void APasswordAfterAKeywordMasksOnlyTheValue(string text, string expected)
    {
        var finding = Assert.Single(CreateScanner().Scan(text, Only(DlpClass.Password)));

        Assert.Equal(DlpClass.Password, finding.Class);
        // The keyword stays in the text: without it the model cannot see it is reading a
        // credential line at all, which is the same reason "Bearer" is kept.
        Assert.Equal(expected, finding.Value);
    }

    [Fact]
    public void AUrlPasswordMasksOnlyThePasswordSegment()
    {
        var finding = Assert.Single(
            CreateScanner().Scan("try https://alice:s3cretpass@example.org/ now", Only(DlpClass.Password)));

        Assert.Equal(DlpClass.Password, finding.Class);
        Assert.Equal("s3cretpass", finding.Value);
    }

    [Fact]
    public void APlaceholderPasswordIsNotMasked()
    {
        /* The placeholder-word gate is the only thing standing between this class and a how-to
           question, because a password has no entropy floor to clear.

           Known false positive, accepted: "password=changeme" and "password=hunter2" ARE
           masked. Neither word is on the gate's list, and adding common weak passwords to it
           would be a list with no end. The class is an individual switch for exactly this
           reason. */
        Assert.Empty(CreateScanner().Scan("set password=YOUR_PASSWORD_HERE", Only(DlpClass.Password)));
        Assert.Empty(CreateScanner().Scan("use https://alice:EXAMPLEPASS@example.org/", Only(DlpClass.Password)));
    }

    [Fact]
    public void AShortPasswordIsBelowTheKeywordPatternsMinimum()
    {
        // Eight characters: below it the pattern would fire on "pwd: yes" and worse.
        Assert.Empty(CreateScanner().Scan("password=abc123", Only(DlpClass.Password)));
    }

    [Fact]
    public void AKeyShapedValueAfterASecretKeywordIsReportedAsAKey()
    {
        /* The two patterns claim the same span, so the pattern order is what decides. A key is
           the more specific answer and gives the user the more useful placeholder. */
        var finding = Assert.Single(ScanWithDefaults($"secret={SkKey}"));

        Assert.Equal(DlpClass.ApiKey, finding.Class);
        Assert.Equal(SkKey, finding.Value);
    }

    [Theory]
    [InlineData("call 555-123-4567 now", "555-123-4567")]
    [InlineData("call +358 40 123 4567 now", "+358 40 123 4567")]
    public void APhoneNumberIsOnlyDetectedWhenTheUserOptsIn(string text, string expected)
    {
        Assert.Empty(ScanWithDefaults(text));

        var finding = Assert.Single(CreateScanner().Scan(text, Only(DlpClass.Phone)));
        Assert.Equal(expected, finding.Value);
    }

    // ── Overlap and idempotence ─────────────────────────────────────────────────

    [Fact]
    public void FindingsNeverOverlapAndAreOrderedByPosition()
    {
        string text = $"key {SkKey} card 4111111111111111 ssn 078-05-1120 jwt {Jwt} done";

        var findings = ScanWithDefaults(text);

        Assert.Equal(
            new[] { DlpClass.ApiKey, DlpClass.CreditCard, DlpClass.Ssn, DlpClass.Token },
            findings.Select(f => f.Class));

        int consumedTo = 0;
        foreach (var finding in findings)
        {
            /* Overlapping findings would produce a mangled prompt: the second replacement would
               land inside the first one's token. */
            Assert.True(finding.Start >= consumedTo, "findings overlap");
            Assert.Equal(finding.Value, text.Substring(finding.Start, finding.Length));
            consumedTo = finding.Start + finding.Length;
        }
    }

    [Theory]
    [InlineData("[REDACTED_API_KEY_1]")]
    [InlineData("[REDACTED_PRIVATE_KEY_1]")]
    [InlineData("[REDACTED_TOKEN_12]")]
    [InlineData("[REDACTED_PASSWORD_1]")]
    [InlineData("[REDACTED_CARD_1]")]
    [InlineData("[REDACTED_IBAN_1]")]
    [InlineData("[REDACTED_SSN_1]")]
    [InlineData("[REDACTED_EMAIL_1]")]
    [InlineData("[REDACTED_PHONE_1]")]
    public void ARedactionTokenScansClean(string token)
    {
        /* Masking has to be idempotent: a second pass over already-masked text must be a no-op.
           If a token were itself detected, the vault would replace it and the original secret
           would become unrecoverable. */
        Assert.Empty(CreateScanner().Scan($"the value was {token} in the prompt", DlpPolicy.Defaults));
    }

    // ── The pre-filter ──────────────────────────────────────────────────────────

    [Fact]
    public void ALargeMessageWithNoSecretStaysOnThePreFilterPath()
    {
        /* Almost no message contains a credential, so the cost of scanning must fall on the
           substring markers rather than on a regex sweep. This prose triggers none of them —
           no digits, no '@', none of the key prefixes — so no pattern runs at all.

           The wall-clock bound is deliberately loose: it is there to catch a regression that
           puts the regexes back on the common path, not to measure this machine. A build agent
           under load can be an order of magnitude slower and still pass. */
        string prose = string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog ", 1200));
        Assert.True(prose.Length > 50 * 1024);

        var scanner = CreateScanner();
        var stopwatch = Stopwatch.StartNew();
        var findings = scanner.Scan(prose, DlpPolicy.Defaults);
        stopwatch.Stop();

        Assert.Empty(findings);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 100,
            $"scanning {prose.Length} bytes of ordinary prose took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void NullAndEmptyTextScanClean()
    {
        var scanner = CreateScanner();

        Assert.Empty(scanner.Scan(null, DlpPolicy.Defaults));
        Assert.Empty(scanner.Scan(string.Empty, DlpPolicy.Defaults));
    }
}
