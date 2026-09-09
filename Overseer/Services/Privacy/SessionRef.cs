using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Overseer.Services.Privacy;

/// <summary>
/// A reference to a chat session that is either persistent — a <c>ChatSession</c> row — or
/// ephemeral, living only in <see cref="EphemeralSessionStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The alternative this replaces was a negative <c>long</c> sentinel: an ephemeral session was
/// to be recognised by <c>sessionId &lt; 0</c>, checked independently in the hub, the
/// controller, the chat service and the ongoing-generation manager. Every one of those checks
/// is invisible to the compiler, and a single missed one is an EF query against a negative
/// primary key — which returns no row, so the failure is silence rather than an error. Making
/// the two cases distinct states of one type turns each of those checks into something the
/// compiler asks about.
/// </para>
/// <para>
/// The wire format is deliberately compatible with what the client already sends: a persistent
/// reference serialises as its decimal id, exactly as before, and an ephemeral one as
/// <c>eph_&lt;guid&gt;</c>. <see cref="GroupName"/> is the same string, so a persistent
/// session's SignalR group is unchanged and an ephemeral session's cannot collide with one.
/// </para>
/// <para>
/// <c>default(SessionRef)</c> is neither state and is reported by <see cref="IsValid"/> as
/// invalid. It exists because a struct always has a default; nothing should produce one, and
/// the accessors throw rather than quietly reporting id 0, which would read as a real session
/// to an EF query.
/// </para>
/// </remarks>
public readonly struct SessionRef : IEquatable<SessionRef>
{
    /// <summary>
    /// Wire prefix marking an ephemeral reference. Chosen so it can never parse as a decimal
    /// id, which is what lets one string field carry both states unambiguously.
    /// </summary>
    public const string EphemeralPrefix = "eph_";

    private readonly long _id;
    private readonly Guid _token;

    private SessionRef(long id, Guid token)
    {
        _id = id;
        _token = token;
    }

    /// <summary>A reference to the persisted session with this primary key.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="id"/> is not positive.</exception>
    public static SessionRef Persistent(long id)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), id, "A persistent session id is positive.");
        return new SessionRef(id, Guid.Empty);
    }

    /// <summary>A reference to the ephemeral session held under this token.</summary>
    /// <exception cref="ArgumentException">If <paramref name="token"/> is empty.</exception>
    public static SessionRef Ephemeral(Guid token)
    {
        if (token == Guid.Empty)
            throw new ArgumentException("An ephemeral session token cannot be empty.", nameof(token));
        return new SessionRef(0, token);
    }

    /// <summary>Mints a reference for a session that does not exist yet.</summary>
    public static SessionRef NewEphemeral() => new SessionRef(0, Guid.NewGuid());

    /// <summary>Whether this reference names a row in <c>ChatSession</c>.</summary>
    public bool IsPersistent => _id > 0;

    /// <summary>Whether this reference names an entry in <see cref="EphemeralSessionStore"/>.</summary>
    public bool IsEphemeral => _token != Guid.Empty;

    /// <summary>False only for <c>default(SessionRef)</c>.</summary>
    public bool IsValid => IsPersistent || IsEphemeral;

    /// <summary>The primary key. Only meaningful for a persistent reference.</summary>
    /// <exception cref="InvalidOperationException">If this reference is not persistent.</exception>
    public long PersistentId => IsPersistent
        ? _id
        : throw new InvalidOperationException(
            $"{ToWireString()} is not a persisted session, so it has no primary key.");

    /// <summary>The store token. Only meaningful for an ephemeral reference.</summary>
    /// <exception cref="InvalidOperationException">If this reference is not ephemeral.</exception>
    public Guid EphemeralToken => IsEphemeral
        ? _token
        : throw new InvalidOperationException(
            $"{ToWireString()} is not an ephemeral session, so it has no store token.");

    /// <summary>
    /// The primary key if this reference has one, otherwise null — for the metadata columns
    /// that record which session produced a row and must be left unset for an ephemeral turn.
    /// </summary>
    public long? PersistentIdOrNull => IsPersistent ? _id : null;

    /// <summary>
    /// Parses the wire form: a positive decimal id, or <c>eph_</c> followed by a GUID.
    /// </summary>
    public static bool TryParse(string? value, out SessionRef result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        value = value.Trim();

        if (value.StartsWith(EphemeralPrefix, StringComparison.Ordinal))
        {
            if (!Guid.TryParseExact(value.Substring(EphemeralPrefix.Length), "D", out var token)
                || token == Guid.Empty)
            {
                return false;
            }
            result = new SessionRef(0, token);
            return true;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return false;
        }

        result = new SessionRef(id, Guid.Empty);
        return true;
    }

    /// <summary>
    /// The wire form. A persistent reference renders as the bare decimal id it always did, so
    /// nothing that already stores or compares one has to change.
    /// </summary>
    public string ToWireString()
    {
        if (IsPersistent) return _id.ToString(CultureInfo.InvariantCulture);
        if (IsEphemeral) return EphemeralPrefix + _token.ToString("D");
        return "(no session)";
    }

    /// <summary>
    /// The SignalR group carrying this session's events. Identical to the wire form, which is
    /// what keeps a persistent session's group name unchanged.
    /// </summary>
    public string GroupName => ToWireString();

    public override string ToString() => ToWireString();

    public bool Equals(SessionRef other) => _id == other._id && _token == other._token;

    public override bool Equals(object? obj) => obj is SessionRef other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_id, _token);

    public static bool operator ==(SessionRef left, SessionRef right) => left.Equals(right);

    public static bool operator !=(SessionRef left, SessionRef right) => !left.Equals(right);

    /// <summary>
    /// Reads a session reference from a request body that carries it either as a JSON string
    /// or, from an older client, as a JSON number.
    /// </summary>
    /// <remarks>
    /// The reference became a string when ephemeral sessions arrived; a browser tab left open
    /// across the deployment still posts <c>"sessionId": 1234</c>, and the default converter
    /// rejects a number for a string property outright. That would surface as a bare 400 on the
    /// user's next message with nothing to explain it, so the converter accepts both forms.
    /// It writes only the string form.
    /// </remarks>
    public sealed class LenientJsonConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var id)
                        ? id.ToString(CultureInfo.InvariantCulture)
                        : throw new JsonException("A numeric session reference must be an integer.");
                default:
                    throw new JsonException($"A session reference must be a string or an integer, not {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }
}
