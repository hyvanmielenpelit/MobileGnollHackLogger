using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ConfidentialityPostureServiceTests
{
    private static ConfidentialityPostureService CreateService(string? adminFloor = null)
    {
        var settings = new Dictionary<string, string?>();
        if (adminFloor != null)
            settings["PrivacySettings:ConfidentialFloor:ModelGate"] = adminFloor;

        return new ConfidentialityPostureService(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static PostureResolution Verified(ProviderConfidentialityPosture posture)
        => new(posture, IsOperatorVerified: true, EstablishedUtc: DateTime.UtcNow);

    private static PostureResolution SelfDeclared(ProviderConfidentialityPosture posture)
        => new(posture, IsOperatorVerified: false, EstablishedUtc: DateTime.UtcNow);

    // ── The ladder ──────────────────────────────────────────────────────────────

    [Fact]
    public void Ladder_IsOrderedWeakestToStrongest()
    {
        var ordered = new[]
        {
            ProviderConfidentialityPosture.Unknown,
            ProviderConfidentialityPosture.Standard,
            ProviderConfidentialityPosture.NoTraining,
            ProviderConfidentialityPosture.ZeroRetention,
            ProviderConfidentialityPosture.PrivateCloud,
            ProviderConfidentialityPosture.SelfHosted
        };

        // The ordering is load-bearing: IsAtLeast and the gate both compare on it.
        Assert.Equal(ordered, ordered.OrderBy(p => (int)p).ToArray());
        Assert.Equal(ordered.Length, Enum.GetValues<ProviderConfidentialityPosture>().Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotAPosture")]
    [InlineData("SomethingFromANewerBuild")]
    public void ParsePosture_UnknownAndUnrecognisedResolveDownwardToUnknown(string? stored)
    {
        /* Resolving down rather than throwing is deliberate: a value written by a newer build,
           or corrupted, must degrade to "nothing is established". Failing upward into a
           stronger posture is the one outcome this ladder exists to prevent. */
        Assert.Equal(
            ProviderConfidentialityPosture.Unknown,
            ProviderConfidentialityPostureExtensions.ParsePosture(stored));
    }

    [Theory]
    [InlineData("ZeroRetention", ProviderConfidentialityPosture.ZeroRetention)]
    [InlineData("zeroretention", ProviderConfidentialityPosture.ZeroRetention)]
    [InlineData("  NoTraining  ", ProviderConfidentialityPosture.NoTraining)]
    [InlineData("SELFHOSTED", ProviderConfidentialityPosture.SelfHosted)]
    public void ParsePosture_AcceptsStoredNamesCaseInsensitively(string stored, ProviderConfidentialityPosture expected)
    {
        Assert.Equal(expected, ProviderConfidentialityPostureExtensions.ParsePosture(stored));
    }

    [Fact]
    public void ParsePosture_RoundTripsEveryStoredValue()
    {
        foreach (var posture in Enum.GetValues<ProviderConfidentialityPosture>())
        {
            Assert.Equal(posture, ProviderConfidentialityPostureExtensions.ParsePosture(posture.ToStoredValue()));
        }
    }

    [Theory]
    [InlineData(ProviderConfidentialityPosture.Unknown, false)]
    [InlineData(ProviderConfidentialityPosture.Standard, false)]
    [InlineData(ProviderConfidentialityPosture.NoTraining, false)]
    [InlineData(ProviderConfidentialityPosture.ZeroRetention, true)]
    [InlineData(ProviderConfidentialityPosture.PrivateCloud, true)]
    [InlineData(ProviderConfidentialityPosture.SelfHosted, true)]
    public void MeetsConfidentialThreshold_StartsAtZeroRetention(
        ProviderConfidentialityPosture posture, bool expected)
    {
        Assert.Equal(expected, posture.MeetsConfidentialThreshold());
    }

    // ── Resolution from the two key holders ─────────────────────────────────────

    [Fact]
    public void ResolveForSystemConfiguration_NullPostureIsUnknownAndUnverified()
    {
        var service = CreateService();

        var resolved = service.ResolveForSystemConfiguration(new SystemAiApiConfiguration());

        Assert.Equal(ProviderConfidentialityPosture.Unknown, resolved.Posture);
        Assert.False(resolved.IsOperatorVerified);
        Assert.False(resolved.IsSelfDeclared);
    }

    [Fact]
    public void ResolveForSystemConfiguration_VerificationIsADateAndNothingElse()
    {
        var service = CreateService();

        var unverified = service.ResolveForSystemConfiguration(new SystemAiApiConfiguration
        {
            ConfidentialityPosture = "ZeroRetention"
        });
        Assert.False(unverified.IsOperatorVerified);
        Assert.True(unverified.IsSelfDeclared);

        var verified = service.ResolveForSystemConfiguration(new SystemAiApiConfiguration
        {
            ConfidentialityPosture = "ZeroRetention",
            PostureVerifiedUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            PostureAgreementRef = "DPA-2026-14",
            DataRegion = "eu-north-1"
        });
        Assert.True(verified.IsOperatorVerified);
        Assert.False(verified.IsSelfDeclared);
        Assert.Equal("DPA-2026-14", verified.AgreementRef);
        Assert.Equal("eu-north-1", verified.DataRegion);
    }

    [Fact]
    public void ResolveForSystemConfiguration_AnUnknownPostureCannotBeVerifiedIntoAnything()
    {
        // There is nothing to verify, so a stray date must not manufacture a claim.
        var service = CreateService();

        var resolved = service.ResolveForSystemConfiguration(new SystemAiApiConfiguration
        {
            ConfidentialityPosture = null,
            PostureVerifiedUtc = DateTime.UtcNow
        });

        Assert.Equal(ProviderConfidentialityPosture.Unknown, resolved.Posture);
        Assert.False(resolved.IsOperatorVerified);
    }

    [Fact]
    public void ResolveForUserKey_IsNeverOperatorVerified()
    {
        /* Overseer cannot see the user's agreement with their provider, so it cannot verify
           it. This is the D-4 defect: gating on a self-asserted trust level would let anyone
           tick ZeroRetention and unlock a claim the product cannot make. */
        var service = CreateService();

        var resolved = service.ResolveForUserKey(new UserAiApiKey
        {
            Provider = "OpenAI",
            ConfidentialityPosture = "SelfHosted",
            PostureDeclaredUtc = DateTime.UtcNow
        });

        Assert.Equal(ProviderConfidentialityPosture.SelfHosted, resolved.Posture);
        Assert.False(resolved.IsOperatorVerified);
        Assert.True(resolved.IsSelfDeclared);
    }

    // ── The effective gate: stricter of admin floor and user choice ─────────────

    [Fact]
    public void EffectiveGate_DefaultAdminFloorIsUserDecides()
    {
        Assert.Equal(ConfidentialityGateMode.UserDecides, CreateService().AdminFloor);
        Assert.Equal(ConfidentialityGateMode.UserDecides, CreateService("nonsense").AdminFloor);
        Assert.Equal(ConfidentialityGateMode.VerifiedPostureOnly, CreateService("VerifiedPostureOnly").AdminFloor);
        Assert.Equal(ConfidentialityGateMode.AskWhenUnclear, CreateService("askwhenunclear").AdminFloor);
    }

    [Theory]
    // floor, user choice, expected effective gate
    [InlineData(ConfidentialityGateMode.UserDecides, null, ConfidentialityGateMode.UserDecides)]
    [InlineData(ConfidentialityGateMode.UserDecides, ConfidentialityGateMode.AskWhenUnclear, ConfidentialityGateMode.AskWhenUnclear)]
    [InlineData(ConfidentialityGateMode.UserDecides, ConfidentialityGateMode.VerifiedPostureOnly, ConfidentialityGateMode.VerifiedPostureOnly)]
    [InlineData(ConfidentialityGateMode.AskWhenUnclear, ConfidentialityGateMode.UserDecides, ConfidentialityGateMode.AskWhenUnclear)]
    [InlineData(ConfidentialityGateMode.VerifiedPostureOnly, ConfidentialityGateMode.UserDecides, ConfidentialityGateMode.VerifiedPostureOnly)]
    [InlineData(ConfidentialityGateMode.VerifiedPostureOnly, ConfidentialityGateMode.AskWhenUnclear, ConfidentialityGateMode.VerifiedPostureOnly)]
    [InlineData(ConfidentialityGateMode.VerifiedPostureOnly, null, ConfidentialityGateMode.VerifiedPostureOnly)]
    public void EffectiveGate_IsTheStricterOfFloorAndUserChoice(
        ConfidentialityGateMode floor, ConfidentialityGateMode? userChoice, ConfidentialityGateMode expected)
    {
        // Invariant 2: a user may tighten, never weaken.
        Assert.Equal(expected, ConfidentialityPostureService.EffectiveGate(floor, userChoice));
    }

    [Fact]
    public void EffectiveGate_NoUserSettingCanReachAWeakerGateThanTheFloor()
    {
        foreach (var floor in Enum.GetValues<ConfidentialityGateMode>())
        {
            foreach (var choice in Enum.GetValues<ConfidentialityGateMode>().Cast<ConfidentialityGateMode?>().Append(null))
            {
                Assert.True(ConfidentialityPostureService.EffectiveGate(floor, choice) >= floor);
            }
        }
    }

    // ── Gate outcomes ───────────────────────────────────────────────────────────

    [Fact]
    public void Gate_UserDecides_AllowsAnUnmarkedKeyWhateverItsPosture()
    {
        var service = CreateService();

        foreach (var posture in Enum.GetValues<ProviderConfidentialityPosture>())
        {
            var result = service.EvaluateGate(
                SelfDeclared(posture), userTrustsForConfidential: null, ConfidentialityGateMode.UserDecides);

            Assert.Equal(ConfidentialityGateOutcome.Allow, result.Outcome);
        }
    }

    [Fact]
    public void Gate_AnExplicitNoRefusesInEveryMode()
    {
        /* False is a decision, not an absence: a user who has said this key is unsuitable
           should not be asked again because the gate happens to be permissive. */
        var service = CreateService();

        foreach (var mode in Enum.GetValues<ConfidentialityGateMode>())
        {
            var result = service.EvaluateGate(Verified(ProviderConfidentialityPosture.SelfHosted), false, mode);

            Assert.Equal(ConfidentialityGateOutcome.Refuse, result.Outcome);
            Assert.Contains("not suitable", result.Reason);
        }
    }

    [Fact]
    public void Gate_AskWhenUnclear_AsksWhileTheKeyIsUndecided_AndTheReasonFollowsThePosture()
    {
        var service = CreateService();

        var unknownPosture = service.EvaluateGate(
            SelfDeclared(ProviderConfidentialityPosture.Unknown), null, ConfidentialityGateMode.AskWhenUnclear);
        Assert.Equal(ConfidentialityGateOutcome.AskOnce, unknownPosture.Outcome);
        Assert.Contains("Nothing is established", unknownPosture.Reason);

        var selfDeclared = service.EvaluateGate(
            SelfDeclared(ProviderConfidentialityPosture.ZeroRetention), null, ConfidentialityGateMode.AskWhenUnclear);
        Assert.Equal(ConfidentialityGateOutcome.AskOnce, selfDeclared.Outcome);
        Assert.Contains("self-declared", selfDeclared.Reason);
    }

    [Fact]
    public void Gate_AskWhenUnclear_AllowsOnceTheKeyHasBeenAccepted()
    {
        // Asked once per key, not once per turn: the answer persists on the key.
        var service = CreateService();

        Assert.Equal(ConfidentialityGateOutcome.Allow, service.EvaluateGate(
            SelfDeclared(ProviderConfidentialityPosture.NoTraining), true,
            ConfidentialityGateMode.AskWhenUnclear).Outcome);

        /* Including the case the question is most often about. An unestablished posture is
           what the user was asked to accept, so re-asking after they accepted it would leave
           the prompt with no answer that ever ends it. */
        Assert.Equal(ConfidentialityGateOutcome.Allow, service.EvaluateGate(
            SelfDeclared(ProviderConfidentialityPosture.Unknown), true,
            ConfidentialityGateMode.AskWhenUnclear).Outcome);
    }

    [Fact]
    public void Gate_VerifiedPostureOnly_AllowsOnlyVerifiedZeroRetentionOrStronger()
    {
        var service = CreateService();

        Assert.Equal(ConfidentialityGateOutcome.Allow, service.EvaluateGate(
            Verified(ProviderConfidentialityPosture.ZeroRetention), null, ConfidentialityGateMode.VerifiedPostureOnly).Outcome);
        Assert.Equal(ConfidentialityGateOutcome.Allow, service.EvaluateGate(
            Verified(ProviderConfidentialityPosture.SelfHosted), null, ConfidentialityGateMode.VerifiedPostureOnly).Outcome);

        // Verified but too weak.
        Assert.Equal(ConfidentialityGateOutcome.Refuse, service.EvaluateGate(
            Verified(ProviderConfidentialityPosture.NoTraining), null, ConfidentialityGateMode.VerifiedPostureOnly).Outcome);
    }

    [Fact]
    public void Gate_VerifiedPostureOnly_RefusesASelfDeclaredPostureAndSaysWhy()
    {
        /* The manual check the plan calls for: the same BYO key that passes under UserDecides
           is refused under the stricter floor, with a refusal that names self-declaration
           rather than just saying no. */
        var service = CreateService();

        var result = service.EvaluateGate(
            SelfDeclared(ProviderConfidentialityPosture.ZeroRetention), true, ConfidentialityGateMode.VerifiedPostureOnly);

        Assert.Equal(ConfidentialityGateOutcome.Refuse, result.Outcome);
        Assert.Contains("self-declared", result.Reason);
        Assert.Contains("cannot verify", result.Reason);
    }

    [Fact]
    public void Gate_VerifiedPostureOnly_RefusalForAnUnknownPostureDoesNotBlameTheUser()
    {
        var service = CreateService();

        var result = service.EvaluateGate(
            PostureResolution.Nothing, null, ConfidentialityGateMode.VerifiedPostureOnly);

        Assert.Equal(ConfidentialityGateOutcome.Refuse, result.Outcome);
        Assert.Contains("Nothing is established", result.Reason);
        Assert.DoesNotContain("self-declared", result.Reason);
    }

    // ── Badge states ────────────────────────────────────────────────────────────

    [Fact]
    public void Badge_IsAbsentWhenConfidentialityModeIsOff()
    {
        var service = CreateService();

        var badge = service.ResolveBadge(
            isConfidential: false,
            Verified(ProviderConfidentialityPosture.SelfHosted),
            ConfidentialControlState.AllActive);

        Assert.Equal(PrivateBadgeState.None, badge.State);
        Assert.Equal(string.Empty, badge.Label);
    }

    [Fact]
    public void Badge_IsGreenForVerifiedZeroRetentionOrStrongerWithEveryControlActive()
    {
        var service = CreateService();

        foreach (var posture in new[]
        {
            ProviderConfidentialityPosture.ZeroRetention,
            ProviderConfidentialityPosture.PrivateCloud,
            ProviderConfidentialityPosture.SelfHosted
        })
        {
            var badge = service.ResolveBadge(true, Verified(posture), ConfidentialControlState.AllActive);

            Assert.Equal(PrivateBadgeState.Green, badge.State);
            Assert.Equal("Private", badge.Label);
            Assert.Equal("Fully protected", badge.Headline);
            Assert.Equal(PostureVerification.Verified, badge.Posture!.Verification);
        }
    }

    [Fact]
    public void Badge_ASelfDeclaredPostureNeverProducesGreen()
    {
        /* Invariant 1, and the reason this test enumerates the whole ladder rather than one
           case: the product may not claim more than is known, however strong the claim the
           user typed. This is the assertion that dies quietly in a refactor. */
        var service = CreateService();

        foreach (var posture in Enum.GetValues<ProviderConfidentialityPosture>())
        {
            var badge = service.ResolveBadge(true, SelfDeclared(posture), ConfidentialControlState.AllActive);

            Assert.NotEqual(PrivateBadgeState.Green, badge.State);
        }
    }

    [Fact]
    public void Badge_IsYellowForSelfDeclaredStrengthAndForVerifiedNoTraining()
    {
        var service = CreateService();

        var selfDeclaredStrong = service.ResolveBadge(
            true, SelfDeclared(ProviderConfidentialityPosture.ZeroRetention), ConfidentialControlState.AllActive);
        Assert.Equal(PrivateBadgeState.Yellow, selfDeclaredStrong.State);
        Assert.Equal(PostureVerification.SelfDeclared, selfDeclaredStrong.Posture!.Verification);

        var verifiedNoTraining = service.ResolveBadge(
            true, Verified(ProviderConfidentialityPosture.NoTraining), ConfidentialControlState.AllActive);
        Assert.Equal(PrivateBadgeState.Yellow, verifiedNoTraining.State);
        Assert.Contains("may still keep", verifiedNoTraining.Explanation);
    }

    [Theory]
    [InlineData(ProviderConfidentialityPosture.Unknown)]
    [InlineData(ProviderConfidentialityPosture.Standard)]
    public void Badge_IsOrangeWhenNothingMeaningfulIsEstablished(ProviderConfidentialityPosture posture)
    {
        var service = CreateService();

        var badge = service.ResolveBadge(true, SelfDeclared(posture), ConfidentialControlState.AllActive);

        Assert.Equal(PrivateBadgeState.Orange, badge.State);
        // Orange is accurate, not a failure: Overseer's own controls do hold.
        Assert.Equal("Protected by Overseer only", badge.Headline);

        /* Orange covers two different provider stories, and the dialog names them apart:
           nothing was ever declared, or the user declared ordinary terms on their own key,
           which establishes nothing about retention either. Neither is ever "verified". */
        Assert.Equal(
            posture == ProviderConfidentialityPosture.Unknown
                ? PostureVerification.NotEstablished
                : PostureVerification.SelfDeclared,
            badge.Posture!.Verification);
    }

    [Fact]
    public void Badge_IsRedWheneverAnyControlIsOff_AndRedOutranksThePosture()
    {
        /* The storage settings are user-adjustable, so this state is reachable by
           configuration rather than by bug -- and the strongest verified agreement is worth
           nothing to a session writing its content in clear. */
        var service = CreateService();

        var strongest = Verified(ProviderConfidentialityPosture.SelfHosted);

        var unencrypted = ConfidentialControlState.AllActive with { ContentEncrypted = false };
        var badge = service.ResolveBadge(true, strongest, unencrypted);
        Assert.Equal(PrivateBadgeState.Red, badge.State);
        Assert.Equal("Not fully protected", badge.Headline);
        Assert.False(badge.Controls.Single(c => c.Key == "contentEncrypted").Active);
        Assert.Contains("message content is not encrypted", badge.Explanation);

        var egressOpen = ConfidentialControlState.AllActive with { ExternalEgressBlocked = false };
        Assert.Equal(PrivateBadgeState.Red, service.ResolveBadge(true, strongest, egressOpen).State);

        // Every control, not only the two the plan names as examples.
        foreach (var state in new[]
        {
            ConfidentialControlState.AllActive with { TitleGenerationSuppressed = false },
            ConfidentialControlState.AllActive with { PromptCacheDisabled = false },
            ConfidentialControlState.AllActive with { ExcludedFromSearch = false }
        })
        {
            Assert.Equal(PrivateBadgeState.Red, service.ResolveBadge(true, strongest, state).State);
        }
    }

    [Fact]
    public void Badge_ControlsListEveryControlWithItsState()
    {
        /* The dialog lists the controls in this order, and the order is asserted rather than
           the set: a control that moved between the badge and the dialog would be read as a
           different control by anyone comparing the two. */
        var service = CreateService();

        var badge = service.ResolveBadge(
            true, Verified(ProviderConfidentialityPosture.ZeroRetention), ConfidentialControlState.AllActive);

        Assert.Equal(5, badge.Controls.Count);
        Assert.All(badge.Controls, c => Assert.True(c.Active));
        Assert.Equal(
            new[]
            {
                "contentEncrypted", "externalEgressBlocked", "titleGenerationSuppressed",
                "promptCacheDisabled", "excludedFromSearch"
            },
            badge.Controls.Select(c => c.Key));
    }

    [Fact]
    public void Badge_PostureCarriesTheDataRegionWhenOneIsKnown()
    {
        var service = CreateService();

        var posture = new PostureResolution(
            ProviderConfidentialityPosture.PrivateCloud,
            IsOperatorVerified: true,
            DataRegion: "eu-north-1");

        var badge = service.ResolveBadge(true, posture, ConfidentialControlState.AllActive);

        Assert.Equal("eu-north-1", badge.Posture!.Region);
    }

    [Fact]
    public void ControlState_ReportsWhatIsOnAndWhatIsOff()
    {
        Assert.True(ConfidentialControlState.AllActive.AllControlsActive);
        Assert.Empty(ConfidentialControlState.AllActive.InactiveControls);
        Assert.Equal(5, ConfidentialControlState.AllActive.ActiveControls.Count);

        Assert.False(ConfidentialControlState.NoneActive.AllControlsActive);
        Assert.Equal(5, ConfidentialControlState.NoneActive.InactiveControls.Count);
        Assert.Empty(ConfidentialControlState.NoneActive.ActiveControls);
    }

    [Fact]
    public void ControlState_DescribesEveryControlWhetherOnOrOff()
    {
        /* Describe() is what the details dialog renders, so it reports all five in both
           directions -- unlike ActiveControls and InactiveControls, which each report one
           side and would leave the dialog with nothing to show for the other. */
        var mixed = ConfidentialControlState.AllActive with { PromptCacheDisabled = false };

        foreach (var state in new[] { ConfidentialControlState.AllActive, ConfidentialControlState.NoneActive, mixed })
        {
            var described = state.Describe();

            Assert.Equal(5, described.Count);
            Assert.All(described, d =>
            {
                Assert.False(string.IsNullOrWhiteSpace(d.Title));
                Assert.False(string.IsNullOrWhiteSpace(d.Description));
            });
        }

        Assert.All(ConfidentialControlState.AllActive.Describe(), d => Assert.True(d.Active));
        Assert.All(ConfidentialControlState.NoneActive.Describe(), d => Assert.False(d.Active));
        Assert.False(mixed.Describe().Single(d => d.Key == "promptCacheDisabled").Active);
        Assert.True(mixed.Describe().Single(d => d.Key == "contentEncrypted").Active);
    }
}
