using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ConfidentialPolicyResolverTests
{
    private static ConfidentialPolicyResolver CreateResolver(Dictionary<string, string?>? floor = null)
        => new(new ConfigurationBuilder().AddInMemoryCollection(floor ?? new Dictionary<string, string?>()).Build());

    // ── A malformed floor must not be fatal ─────────────────────────────────────

    [Fact]
    public void AMalformedFloorValueFallsBackInsteadOfTakingTheApplicationDown()
    {
        /* ConfigurationBinder.GetValue throws on a value it cannot convert, and this resolver
           is a DI singleton -- so "yes" instead of true used to fail startup from inside its
           constructor, with an error naming dependency injection rather than the setting.
           Every other malformed input in this framework is reported and defaulted. */
        var floor = CreateResolver(new Dictionary<string, string?>
        {
            { "PrivacySettings:ConfidentialFloor:DisableToolEgress", "yes" },
            { "PrivacySettings:ConfidentialFloor:DisableTitleGeneration", "" },
            { "PrivacySettings:ConfidentialFloor:DisablePromptCache", "1" },
            { "PrivacySettings:ConfidentialFloor:ImmediatePurge", "off" },
            { "PrivacySettings:ConfidentialFloor:RetentionDays", "thirty" },
            { "PrivacySettings:ConfidentialFloor:Persistence", "Nonsense" },
            { "PrivacySettings:ConfidentialFloor:ModelGate", "Nonsense" }
        }).Floor;

        // And it falls back to the STRICT end, so a typo cannot quietly weaken the floor.
        Assert.True(floor.DisableToolEgress);
        Assert.True(floor.DisableTitleGeneration);
        Assert.True(floor.DisablePromptCache);
        Assert.True(floor.ImmediatePurge);
        Assert.Equal(30, floor.RetentionDays);
        Assert.Equal(ConfidentialPersistence.Encrypted, floor.Persistence);
    }

    [Fact]
    public void AWellFormedFloorValueIsStillRead()
    {
        // The control: a fallback that swallowed everything would satisfy the test above.
        var floor = CreateResolver(new Dictionary<string, string?>
        {
            { "PrivacySettings:ConfidentialFloor:DisableToolEgress", "false" },
            { "PrivacySettings:ConfidentialFloor:RetentionDays", "7" }
        }).Floor;

        Assert.False(floor.DisableToolEgress);
        Assert.Equal(7, floor.RetentionDays);
    }

    // ── Defaults ────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultFloor_IsTheDocumentedSetOfDefaults()
    {
        var floor = CreateResolver().Floor;

        Assert.Equal(ConfidentialPersistence.Encrypted, floor.Persistence);
        Assert.Equal(30, floor.RetentionDays);
        Assert.True(floor.DisableToolEgress);
        Assert.True(floor.DisableTitleGeneration);
        Assert.True(floor.DisablePromptCache);
        Assert.True(floor.ImmediatePurge);
        Assert.Equal(ConfidentialityGateMode.UserDecides, floor.ModelGate);
    }

    [Fact]
    public void NullSettings_ResolveToTheFloor()
    {
        var resolver = CreateResolver();

        Assert.Equal(resolver.Floor, resolver.Resolve(null));
    }

    // ── Stricter-of-two, per value ──────────────────────────────────────────────

    [Fact]
    public void AUserCannotWeakenPersistenceBelowTheFloor()
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            { "PrivacySettings:ConfidentialFloor:Persistence", "Encrypted" }
        });

        var resolved = resolver.Resolve(new UserAiSettings { ConfidentialPersistence = "Plaintext" });

        Assert.Equal(ConfidentialPersistence.Encrypted, resolved.Persistence);
    }

    [Fact]
    public void AUserCanTightenPersistenceAboveTheFloor()
    {
        var resolver = CreateResolver();

        var resolved = resolver.Resolve(new UserAiSettings { ConfidentialPersistence = "Ephemeral" });

        Assert.Equal(ConfidentialPersistence.Ephemeral, resolved.Persistence);
    }

    [Fact]
    public void ForRetentionSmallerIsStricter_SoTheComparisonInverts()
    {
        /* The one value where the direction flips: fewer days of retention is a stronger
           promise. Getting it backwards would let a user extend retention past the
           administrator's ceiling, which is the opposite of what a floor is for. */
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            { "PrivacySettings:ConfidentialFloor:RetentionDays", "30" }
        });

        // A user asking for less than the floor gets less.
        Assert.Equal(7, resolver.Resolve(new UserAiSettings { ConfidentialRetentionDays = 7 }).RetentionDays);

        // A user asking for more than the floor does not get more.
        Assert.Equal(30, resolver.Resolve(new UserAiSettings { ConfidentialRetentionDays = 365 }).RetentionDays);
    }

    [Theory]
    [InlineData(true, null, true)]     // floor on, user unset -> on
    [InlineData(true, false, true)]    // floor on, user off   -> on (cannot weaken)
    [InlineData(true, true, true)]
    [InlineData(false, null, false)]   // floor off, user unset -> off
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]    // floor off, user on   -> on (may tighten)
    public void ForTheBooleansTrueIsStricter(bool floorValue, bool? userValue, bool expected)
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            { "PrivacySettings:ConfidentialFloor:DisableToolEgress", floorValue ? "true" : "false" }
        });

        var resolved = resolver.Resolve(new UserAiSettings { ConfidentialDisableToolEgress = userValue });

        Assert.Equal(expected, resolved.DisableToolEgress);
    }

    [Fact]
    public void TheModelGateIsAlsoTheStricterOfTheTwo()
    {
        var permissiveFloor = CreateResolver();
        Assert.Equal(
            ConfidentialityGateMode.VerifiedPostureOnly,
            permissiveFloor.Resolve(new UserAiSettings { ConfidentialModelGate = "VerifiedPostureOnly" }).ModelGate);

        var strictFloor = CreateResolver(new Dictionary<string, string?>
        {
            { "PrivacySettings:ConfidentialFloor:ModelGate", "VerifiedPostureOnly" }
        });
        Assert.Equal(
            ConfidentialityGateMode.VerifiedPostureOnly,
            strictFloor.Resolve(new UserAiSettings { ConfidentialModelGate = "UserDecides" }).ModelGate);
    }

    [Fact]
    public void NoUserSettingCanProduceAPolicyWeakerThanTheFloorInAnyDimension()
    {
        var resolver = CreateResolver(new Dictionary<string, string?>
        {
            { "PrivacySettings:ConfidentialFloor:Persistence", "Encrypted" },
            { "PrivacySettings:ConfidentialFloor:RetentionDays", "14" },
            { "PrivacySettings:ConfidentialFloor:DisableToolEgress", "true" },
            { "PrivacySettings:ConfidentialFloor:DisableTitleGeneration", "true" },
            { "PrivacySettings:ConfidentialFloor:DisablePromptCache", "true" },
            { "PrivacySettings:ConfidentialFloor:ImmediatePurge", "true" },
            { "PrivacySettings:ConfidentialFloor:ModelGate", "AskWhenUnclear" }
        });

        // Everything the user could set to its weakest.
        var resolved = resolver.Resolve(new UserAiSettings
        {
            ConfidentialPersistence = "Plaintext",
            ConfidentialRetentionDays = 999,
            ConfidentialDisableToolEgress = false,
            ConfidentialDisableTitleGeneration = false,
            ConfidentialDisablePromptCache = false,
            ConfidentialImmediatePurge = false,
            ConfidentialModelGate = "UserDecides"
        });

        Assert.Equal(ConfidentialPersistence.Encrypted, resolved.Persistence);
        Assert.Equal(14, resolved.RetentionDays);
        Assert.True(resolved.DisableToolEgress);
        Assert.True(resolved.DisableTitleGeneration);
        Assert.True(resolved.DisablePromptCache);
        Assert.True(resolved.ImmediatePurge);
        Assert.Equal(ConfidentialityGateMode.AskWhenUnclear, resolved.ModelGate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotAPersistenceMode")]
    public void AnUnparseablePersistenceValueDoesNotResolveDownToPlaintext(string? stored)
    {
        /* Unlike the posture ladder, this resolves to the *default* rather than the weakest.
           Resolving down would mean a corrupt value silently produced a plaintext confidential
           session — the exact outcome this setting exists to prevent. */
        Assert.Null(ConfidentialPolicyResolver.ParsePersistence(stored));

        var resolved = CreateResolver().Resolve(new UserAiSettings { ConfidentialPersistence = stored });
        Assert.Equal(ConfidentialPersistence.Encrypted, resolved.Persistence);
    }

    // ── The snapshot, which is what stops a promise moving ──────────────────────

    [Fact]
    public void ApplyToSession_WritesTheJsonAndBothMaterialisedScalars()
    {
        var session = new ChatSession { Id = 1, IsConfidential = true };
        var policy = ConfidentialPolicy.Defaults with { RetentionDays = 7, ImmediatePurge = true };

        ConfidentialPolicyResolver.ApplyToSession(session, policy);

        Assert.NotNull(session.ConfidentialPolicyJson);
        /* The scalars are what the set-based retention sweep can actually filter on; the JSON
           alone would be unqueryable there. */
        Assert.Equal(7, session.EffectiveRetentionDays);
        Assert.True(session.ImmediatePurgeOnDelete);
    }

    [Fact]
    public void ReadSnapshot_RoundTripsAPolicy()
    {
        var session = new ChatSession();
        var policy = new ConfidentialPolicy
        {
            Persistence = ConfidentialPersistence.Ephemeral,
            RetentionDays = 3,
            DisableToolEgress = false,
            DisableTitleGeneration = true,
            DisablePromptCache = false,
            ImmediatePurge = true,
            ModelGate = ConfidentialityGateMode.VerifiedPostureOnly
        };

        ConfidentialPolicyResolver.ApplyToSession(session, policy);

        Assert.Equal(policy, ConfidentialPolicyResolver.ReadSnapshot(session));
    }

    [Fact]
    public void ReadSnapshot_FallsBackToDefaultsRatherThanToTodaysSettings()
    {
        /* A session with no snapshot predates the column. It resolves to the defaults, not to
           whatever the user's settings happen to say now — the whole point of snapshotting is
           that the session's promise does not move. */
        Assert.Equal(ConfidentialPolicy.Defaults, ConfidentialPolicyResolver.ReadSnapshot(new ChatSession()));
        Assert.Equal(
            ConfidentialPolicy.Defaults,
            ConfidentialPolicyResolver.ReadSnapshot(new ChatSession { ConfidentialPolicyJson = "{ not json" }));
    }

    // ── The bridge to the badge ─────────────────────────────────────────────────

    [Fact]
    public void ADefaultPolicyLeavesEveryControlActive()
    {
        var state = ConfidentialPolicy.Defaults.ToControlState();

        Assert.True(state.AllControlsActive);
        Assert.Empty(state.InactiveControls);
    }

    [Fact]
    public void PlaintextPersistenceTurnsOffEncryptionAndSearchExclusionTogether()
    {
        /* Both follow persistence, and for the same reason: there is nothing to hide from a
           query that the stored row does not already show in clear. */
        var state = (ConfidentialPolicy.Defaults with { Persistence = ConfidentialPersistence.Plaintext })
            .ToControlState();

        Assert.False(state.ContentEncrypted);
        Assert.False(state.ExcludedFromSearch);
        Assert.False(state.AllControlsActive);
    }

    [Fact]
    public void TurningOffEgressBlockingIsReportedAsAnInactiveControl()
    {
        var state = (ConfidentialPolicy.Defaults with { DisableToolEgress = false }).ToControlState();

        Assert.False(state.ExternalEgressBlocked);
        Assert.Contains("external tool egress is not blocked", state.InactiveControls);
    }
}

/// <summary>
/// The egress lockout, which the plan calls the easiest thing in the stage to miss.
/// </summary>
public class ConfidentialEgressLockoutTests
{
    [Fact]
    public void TheEgressFlagSurvivesCloneFor()
    {
        /* This is what carries the lockout into a sub-agent. A sub-agent inheriting a
           permissive tool set would reopen the channel the mode exists to close. */
        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1),
            UserId = "u1",
            BlockExternalEgress = true
        };

        var clone = context.CloneFor("call-1");

        Assert.True(clone.BlockExternalEgress);
        Assert.Equal("call-1", clone.ToolCallId);
    }

    [Fact]
    public void CloneFor_DoesNotInventTheFlagWhenItWasNotSet()
    {
        var clone = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1) }.CloneFor("call-1");

        Assert.False(clone.BlockExternalEgress);
    }
}

/// <summary>
/// The <see cref="ConfidentialExecutionScope"/> half of the telemetry drop.
/// </summary>
/// <remarks>
/// An AsyncLocal rather than a Sentry scope tag, because a scope tag only works if a
/// DI-registered processor observes scope tags *and* the SDK applies scope before running
/// processors — an unversioned internal ordering contract. These tests are the reason that
/// choice is checkable at all: none of them needs a Sentry harness.
/// </remarks>
public class ConfidentialExecutionScopeTests
{
    [Fact]
    public void TheFlagIsOffByDefault()
    {
        Assert.False(ConfidentialExecutionScope.IsConfidential);
    }

    [Fact]
    public void EnterSetsTheFlagAndDisposeRestoresIt()
    {
        using (ConfidentialExecutionScope.Enter(true))
        {
            Assert.True(ConfidentialExecutionScope.IsConfidential);
        }

        Assert.False(ConfidentialExecutionScope.IsConfidential);
    }

    [Fact]
    public void EnterFalseInsideAConfidentialScopeCannotClearIt()
    {
        // Nesting must not be able to relax the guarantee.
        using (ConfidentialExecutionScope.Enter(true))
        {
            using (ConfidentialExecutionScope.Enter(false))
            {
                Assert.True(ConfidentialExecutionScope.IsConfidential);
            }

            Assert.True(ConfidentialExecutionScope.IsConfidential);
        }
    }

    [Fact]
    public async Task TheFlagSurvivesAwaitsAndThreadHops()
    {
        /* The property the whole design rests on: a crash raised deep inside a streaming turn,
           on whatever thread the continuation landed on, still sees the flag. */
        using (ConfidentialExecutionScope.Enter(true))
        {
            await Task.Yield();
            Assert.True(ConfidentialExecutionScope.IsConfidential);

            await Task.Run(
                () => Assert.True(ConfidentialExecutionScope.IsConfidential),
                TestContext.Current.CancellationToken);

            await Task.Delay(1, TestContext.Current.CancellationToken);
            Assert.True(ConfidentialExecutionScope.IsConfidential);
        }

        Assert.False(ConfidentialExecutionScope.IsConfidential);
    }

    [Fact]
    public async Task TheFlagDoesNotLeakIntoAnUnrelatedParallelFlow()
    {
        var confidential = Task.Run(async () =>
        {
            using (ConfidentialExecutionScope.Enter(true))
            {
                await Task.Delay(20);
                return ConfidentialExecutionScope.IsConfidential;
            }
        });

        var normal = Task.Run(async () =>
        {
            await Task.Delay(10);
            return ConfidentialExecutionScope.IsConfidential;
        });

        Assert.True(await confidential);
        Assert.False(await normal);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var scope = ConfidentialExecutionScope.Enter(true);
        scope.Dispose();
        scope.Dispose();

        Assert.False(ConfidentialExecutionScope.IsConfidential);
    }
}
