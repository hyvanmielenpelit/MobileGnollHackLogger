using System;
using System.Collections.Generic;
using System.Text.Json;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The typed session reference that replaced identifying an ephemeral session by a negative
/// primary key.
/// </summary>
public class SessionRefTests
{
    [Fact]
    public void APersistentReferenceSerialisesAsTheBareIdItAlwaysWas()
    {
        var reference = SessionRef.Persistent(1234);

        Assert.Equal("1234", reference.ToWireString());
        // The wire compatibility that matters most: a persisted session's SignalR group name is
        // unchanged, so a client mid-stream across a deployment keeps receiving its events.
        Assert.Equal("1234", reference.GroupName);
        Assert.True(reference.IsPersistent);
        Assert.False(reference.IsEphemeral);
        Assert.Equal(1234, reference.PersistentId);
    }

    [Fact]
    public void AnEphemeralReferenceRoundTripsThroughItsWireForm()
    {
        var minted = SessionRef.NewEphemeral();

        Assert.True(SessionRef.TryParse(minted.ToWireString(), out var parsed));
        Assert.Equal(minted, parsed);
        Assert.True(parsed.IsEphemeral);
        Assert.False(parsed.IsPersistent);
        Assert.StartsWith("eph_", parsed.ToWireString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9223372036854775807")]
    public void ADecimalIdParsesAsPersistent(string wire)
    {
        Assert.True(SessionRef.TryParse(wire, out var parsed));
        Assert.True(parsed.IsPersistent);
        Assert.Equal(wire, parsed.ToWireString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    // The sentinel this design replaced. It must not parse: a negative id reaching an EF query
    // returns no row, so the failure would be silence.
    [InlineData("-1")]
    [InlineData("-9999")]
    [InlineData("12.5")]
    [InlineData("abc")]
    [InlineData("eph_")]
    [InlineData("eph_not-a-guid")]
    [InlineData("eph_00000000-0000-0000-0000-000000000000")]
    [InlineData("1234; DROP TABLE ChatSession")]
    public void AnythingElseIsRefused(string? wire)
    {
        Assert.False(SessionRef.TryParse(wire, out var parsed));
        Assert.False(parsed.IsValid);
    }

    [Fact]
    public void TheDefaultIsNeitherKindAndSaysSoRatherThanReportingIdZero()
    {
        SessionRef unset = default;

        Assert.False(unset.IsValid);
        Assert.False(unset.IsPersistent);
        Assert.False(unset.IsEphemeral);
        Assert.Null(unset.PersistentIdOrNull);
        // Zero would read as a real session to an EF query, so the accessor refuses instead.
        Assert.Throws<InvalidOperationException>(() => unset.PersistentId);
        Assert.Throws<InvalidOperationException>(() => unset.EphemeralToken);
    }

    [Fact]
    public void AccessorsRefuseTheOtherKind()
    {
        Assert.Throws<InvalidOperationException>(() => SessionRef.Persistent(7).EphemeralToken);
        Assert.Throws<InvalidOperationException>(() => SessionRef.NewEphemeral().PersistentId);
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionRef.Persistent(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionRef.Persistent(-1));
        Assert.Throws<ArgumentException>(() => SessionRef.Ephemeral(Guid.Empty));
    }

    [Fact]
    public void ItWorksAsADictionaryKey()
    {
        // OngoingChatManager is keyed by this, so value equality is load-bearing rather than
        // incidental.
        var token = Guid.NewGuid();
        var map = new Dictionary<SessionRef, string>
        {
            [SessionRef.Persistent(5)] = "persisted",
            [SessionRef.Ephemeral(token)] = "ephemeral"
        };

        Assert.Equal("persisted", map[SessionRef.Persistent(5)]);
        Assert.Equal("ephemeral", map[SessionRef.Ephemeral(token)]);
        Assert.False(map.ContainsKey(SessionRef.Persistent(6)));
        Assert.False(map.ContainsKey(SessionRef.Ephemeral(Guid.NewGuid())));
        Assert.Equal(SessionRef.Persistent(5), SessionRef.Persistent(5));
        Assert.NotEqual(SessionRef.Persistent(5), SessionRef.Persistent(6));
    }

    [Fact]
    public void AnEphemeralGroupNameCannotCollideWithAPersistedOne()
    {
        var persisted = SessionRef.Persistent(1);
        var ephemeral = SessionRef.NewEphemeral();

        Assert.NotEqual(persisted.GroupName, ephemeral.GroupName);
        Assert.False(SessionRef.TryParse(persisted.GroupName, out var back) && back.IsEphemeral);
    }

    /* ASP.NET Core's own options are camelCase and case-insensitive; a bare
       JsonSerializer.Deserialize is neither, so the property would simply never bind and the
       converter under test would never run. */
    private static readonly JsonSerializerOptions BodyOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class Body
    {
        [System.Text.Json.Serialization.JsonConverter(typeof(SessionRef.LenientJsonConverter))]
        public string? SessionId { get; set; }
    }

    [Theory]
    // A browser tab left open across the deployment still posts the id as a JSON number. It
    // must not turn into a bare 400 on the user's next message.
    [InlineData("{\"sessionId\":1234}", "1234")]
    [InlineData("{\"sessionId\":\"1234\"}", "1234")]
    [InlineData("{\"sessionId\":\"eph_11111111-2222-3333-4444-555555555555\"}", "eph_11111111-2222-3333-4444-555555555555")]
    [InlineData("{\"sessionId\":null}", null)]
    public void TheRequestConverterAcceptsBothWireForms(string json, string? expected)
    {
        var body = JsonSerializer.Deserialize<Body>(json, BodyOptions);

        Assert.NotNull(body);
        Assert.Equal(expected, body!.SessionId);
    }

    [Fact]
    public void TheRequestConverterRefusesAnythingElse()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Body>("{\"sessionId\":12.5}", BodyOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Body>("{\"sessionId\":true}", BodyOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Body>("{\"sessionId\":{}}", BodyOptions));
    }
}
