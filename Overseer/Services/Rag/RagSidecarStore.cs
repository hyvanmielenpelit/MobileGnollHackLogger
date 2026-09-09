using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;

namespace Overseer.Services.Rag;

/// <summary>
/// One document's chunks and embeddings, stored beside the attachment they came from.
/// </summary>
public sealed record RagSidecar
{
    /// <summary>Format version, so a later reader can recognise an older file.</summary>
    public int Version { get; init; } = 1;

    /// <summary>The uploader's display filename, for a human reading the directory.</summary>
    public string FileName { get; init; } = "";

    /// <summary>The embedding engine that produced <see cref="Embeddings"/>, or "none".</summary>
    public string EmbeddingModel { get; init; } = "none";

    public List<RagSidecarChunk> Chunks { get; init; } = new();

    /// <summary>One vector per chunk, or empty when no model was available.</summary>
    public List<float[]> Embeddings { get; init; } = new();
}

/// <param name="Index">Position in the document's chunk sequence.</param>
/// <param name="Text">Verbatim document text.</param>
/// <param name="TokenCount">Estimated tokens.</param>
/// <param name="SourceOffset">Character offset in the extracted document.</param>
public sealed record RagSidecarChunk(int Index, string Text, int TokenCount, int SourceOffset);

/// <summary>
/// Reads and writes RAG sidecars, applying the same protections the attachment itself gets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chunk text and embeddings are content.</b> Chunk text is verbatim document text, and an
/// embedding vector is invertible enough that treating it as anything less would be wishful.
/// So a sidecar is encrypted with the session's own DEK in a confidential session, is never
/// written for an ephemeral one, and lives <i>inside the session directory</i> — which is what
/// makes it disappear through the recursive deletes that already remove attachments, in the
/// purge, in the orphan sweep and in account deletion, rather than needing a fourth code path
/// that could be forgotten.
/// </para>
/// <para>
/// <b>Writing is off by default</b> (<c>RagSettings:WriteSidecars</c>), and that is a
/// deliberate answer to a real question rather than caution for its own sake: nothing reads a
/// sidecar yet. An uploaded document reaches the model on the turn it is attached and is not
/// replayed on later turns — the stored user message holds what the user typed, not the
/// document — so a sidecar today would put content on disk for no consumer, which is precisely
/// the trade this class exists to be careful about. The capability and its whole lifecycle are
/// here and tested, so whichever change first wants cross-turn retrieval finds the privacy work
/// already done and only has to turn the flag on.
/// </para>
/// </remarks>
public sealed class RagSidecarStore
{
    /// <summary>Suffix for the sidecar of a stored attachment.</summary>
    public const string SidecarSuffix = ".rag.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Compact: a sidecar for a long document is mostly float arrays.
        WriteIndented = false
    };

    private readonly ContentProtectionService _contentProtection;
    private readonly ILogger<RagSidecarStore>? _logger;

    public RagSidecarStore(
        IConfiguration configuration,
        ContentProtectionService contentProtection,
        ILogger<RagSidecarStore>? logger = null)
    {
        _contentProtection = contentProtection;
        _logger = logger;

        /* Parsed by hand rather than through ConfigurationBinder.GetValue, which throws on a
           value it cannot convert -- and this is resolved during a turn, where a typo in
           appsettings.json should not become a failed chat. */
        WriteEnabled = bool.TryParse(configuration["RagSettings:WriteSidecars"]?.Trim(), out bool write) && write;
    }

    /// <summary>Whether sidecars are written at all. See the class remarks for why this is off by default.</summary>
    public bool WriteEnabled { get; }

    /// <summary>The path a sidecar takes for an attachment stored at <paramref name="attachmentRelativePath"/>.</summary>
    /// <remarks>
    /// Derived from the attachment's own stored name, so it lands in the same session directory
    /// and is removed by the same recursive delete. The <c>.enc</c> suffix of an encrypted
    /// attachment is dropped first, because the sidecar carries its own.
    /// </remarks>
    public static string SidecarRelativePath(string attachmentRelativePath)
    {
        string basePath = attachmentRelativePath.EndsWith(
            ContentProtectionService.EncryptedFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? attachmentRelativePath[..^ContentProtectionService.EncryptedFileSuffix.Length]
            : attachmentRelativePath;

        return basePath + SidecarSuffix;
    }

    /// <summary>
    /// Writes a sidecar, or does nothing when writing is disabled, the session is ephemeral, or
    /// there is no storage location.
    /// </summary>
    /// <returns>The relative path written, or null when nothing was written.</returns>
    public async Task<string?> TryWriteAsync(
        string? baseDirectory,
        string attachmentRelativePath,
        ChatSession session,
        bool isEphemeralSession,
        bool encrypt,
        string fileName,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<float[]> embeddings,
        string embeddingModel,
        CancellationToken cancellationToken)
    {
        /* An ephemeral session writes no file at all, and this is not an exception to that:
           it is the same rule. A sidecar is a file. */
        if (!WriteEnabled || isEphemeralSession) return null;
        if (string.IsNullOrEmpty(baseDirectory) || chunks.Count == 0) return null;

        try
        {
            var sidecar = new RagSidecar
            {
                FileName = fileName,
                EmbeddingModel = embeddingModel,
                Chunks = chunks
                    .Select(c => new RagSidecarChunk(c.Index, c.Text, c.TokenCount, c.SourceOffset))
                    .ToList(),
                Embeddings = embeddings.ToList()
            };

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(sidecar, SerializerOptions);

            string relativePath = SidecarRelativePath(attachmentRelativePath);
            if (encrypt)
            {
                relativePath += ContentProtectionService.EncryptedFileSuffix;
                payload = _contentProtection.EncryptFile(session, payload);
            }

            string fullPath = Path.Combine(baseDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, payload, cancellationToken);
            return relativePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            /* A sidecar is an optimisation, so failing to write one must not fail the turn. The
               document has already reached the model by this point. */
            _logger?.LogWarning(ex, "Could not write the retrieval sidecar for {FileName}.", fileName);
            return null;
        }
    }

    /// <summary>
    /// Reads a sidecar, transparently decrypting it. Returns null when it is absent, unreadable,
    /// or belongs to a session whose key has been shredded.
    /// </summary>
    public async Task<RagSidecar?> TryReadAsync(
        string? baseDirectory,
        string attachmentRelativePath,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(baseDirectory)) return null;

        string plainPath = SidecarRelativePath(attachmentRelativePath);
        string encryptedPath = plainPath + ContentProtectionService.EncryptedFileSuffix;

        try
        {
            /* The encrypted name is tried first, but the magic bytes are what the decryptor
               trusts: a session upgraded mid-conversation legitimately holds both kinds, and a
               suffix can be wrong where a header cannot. */
            string? found = null;
            foreach (string candidate in new[] { encryptedPath, plainPath })
            {
                string full = Path.Combine(baseDirectory, candidate);
                if (File.Exists(full)) { found = full; break; }
            }

            if (found == null) return null;

            byte[] payload = await File.ReadAllBytesAsync(found, cancellationToken);
            payload = _contentProtection.DecryptFile(session, payload);
            if (payload.Length == 0) return null;

            return JsonSerializer.Deserialize<RagSidecar>(payload, SerializerOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not read the retrieval sidecar at {Path}.", plainPath);
            return null;
        }
    }
}
