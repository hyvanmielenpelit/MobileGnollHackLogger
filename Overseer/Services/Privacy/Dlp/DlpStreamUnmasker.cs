using System;
using System.Text;

namespace Overseer.Services.Privacy.Dlp;

/// <summary>
/// Unmasks a streamed reply on its way to the browser, holding back only the trailing
/// characters that could still turn into a placeholder token.
/// </summary>
/// <remarks>
/// A token can arrive split across chunks — <c>[REDACTED_</c> in one and <c>API_KEY_1]</c> in
/// the next — so replacing per chunk would emit the placeholder to the user verbatim and let
/// them see that a placeholder existed at all. Instead everything up to a candidate <c>[</c> is
/// emitted immediately and the fragment from that bracket is buffered until its <c>]</c>
/// arrives, or until the fragment grows past the longest token a vault can produce and can
/// therefore no longer be one.
///
/// One instance follows one stream and is not thread-safe: a stream is consumed in order.
/// </remarks>
public sealed class DlpStreamUnmasker
{
    private readonly DlpTokenVault _vault;
    private readonly StringBuilder _held = new();

    public DlpStreamUnmasker(DlpTokenVault vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <summary>
    /// Text safe to emit now. Holds back a trailing fragment that could still become a token.
    /// </summary>
    /// <param name="chunk">The next streamed chunk. Null and empty emit nothing.</param>
    public string Push(string? chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            return string.Empty;

        /* A vault with nothing in it can have nothing to restore, so the common case does no
           buffering and no copying at all. Re-checked per chunk rather than cached, because a
           tool result masked mid-turn can put the first secret into the vault after the reply
           has already started streaming. */
        if (_held.Length == 0 && _vault.IsEmpty)
            return chunk;

        _held.Append(chunk);
        return Drain();
    }

    /// <summary>
    /// Everything still held back. Must be called when the stream ends.
    /// </summary>
    /// <remarks>
    /// At the end of a turn the window still holds whatever trailing characters could have been
    /// the start of a token. Skipping this call drops the last few characters of the reply — up
    /// to the longest token, and only when the reply happens to end in bracket-like text, so
    /// the loss is intermittent and looks like a model truncation rather than a bug here.
    /// </remarks>
    public string Flush()
    {
        if (_held.Length == 0)
            return string.Empty;

        /* Whatever is left starts with '[' and has no ']' — Drain would have resolved or
           released it otherwise — so it cannot contain a complete token and is emitted as it
           stands. */
        string tail = _held.ToString();
        _held.Clear();
        return tail;
    }

    private string Drain()
    {
        int maxToken = _vault.MaxTokenLength;
        StringBuilder? output = null;

        while (_held.Length > 0)
        {
            int open = IndexOf(_held, '[', 0);

            if (open < 0)
            {
                // Nothing left that could start a token.
                Emit(ref output, _held.ToString());
                _held.Clear();
                break;
            }

            if (open > 0)
            {
                Emit(ref output, _held.ToString(0, open));
                _held.Remove(0, open);
            }

            // '[' is now at index 0.
            int close = IndexOf(_held, ']', 1);

            if (close > 0 && close < maxToken)
            {
                string candidate = _held.ToString(0, close + 1);
                // An unknown token-shaped string comes back unchanged, which is what keeps
                // ordinary bracketed prose intact.
                Emit(ref output, _vault.Unmask(candidate) ?? candidate);
                _held.Remove(0, close + 1);
                continue;
            }

            if (_held.Length > maxToken)
            {
                /* Either no ']' at all or one too far away: this bracket is ordinary text. The
                   bracket alone is released so the scan can continue at the next one, which is
                   also what stops the buffer growing without bound. */
                Emit(ref output, "[");
                _held.Remove(0, 1);
                continue;
            }

            // Still short enough to become a token: wait for more.
            break;
        }

        return output?.ToString() ?? string.Empty;
    }

    private static void Emit(ref StringBuilder? output, string text)
    {
        if (text.Length == 0)
            return;

        (output ??= new StringBuilder()).Append(text);
    }

    private static int IndexOf(StringBuilder buffer, char value, int start)
    {
        for (int i = start; i < buffer.Length; i++)
        {
            if (buffer[i] == value)
                return i;
        }

        return -1;
    }
}
