using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Overseer.Services.CorpusIndexing;

/// <summary>The three document corpora the chat's wiki and knowledge-base tools read.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CorpusKind
{
    GnollHackWiki,
    NetHackWiki,
    KnowledgeBase
}

/// <summary>How a corpus index is refreshed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CorpusRefreshMode
{
    /// <summary>At startup, on a new HEAD found by the periodic Git check, and on request.</summary>
    GitPoll,

    /// <summary>At startup and on request only.</summary>
    StartupAndManual
}

/// <summary>What started a corpus indexing pass.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CorpusIndexTrigger
{
    Startup,
    RepositoryChange,
    Manual,
    FollowUp
}

/// <summary>The step a corpus indexing pass is in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CorpusIndexPhase
{
    Idle,
    ReadingFiles,
    BuildingIndex,
    Publishing
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CorpusIndexOutcome
{
    Running,
    Succeeded,
    Failed,
    Canceled
}

/// <summary>The answer to a request for a new pass.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CorpusIndexStartResult
{
    Started,
    AlreadyRunning,
    NotConfigured,
    PathMissing
}

/// <summary>One corpus indexing pass, running or finished, as the status API reports it.</summary>
/// <param name="FilesTotal">The number of files the pass reads; null until the files are enumerated.</param>
public sealed record CorpusIndexJob(
    string Id,
    CorpusIndexTrigger Trigger,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    CorpusIndexPhase Phase,
    int FilesRead,
    int? FilesTotal,
    CorpusIndexOutcome Outcome,
    string? Message);

/// <summary>A file a pass left out because it exceeds the corpus's size limit.</summary>
public sealed record SkippedCorpusFileDto(string Path, long Bytes);

/// <summary>A file a pass could not read; <c>Error</c> is the exception type and message.</summary>
public sealed record FailedCorpusFileDto(string Path, string Error);

/// <summary>The caps on the file lists of a corpus status.</summary>
public static class CorpusIndexLimits
{
    /// <summary>The most entries <see cref="CorpusIndexInfoDto.SkippedBySize"/> and <see cref="CorpusIndexInfoDto.FailedFiles"/> hold.</summary>
    public const int MaxListedFiles = 200;

    /// <summary>The length of <see cref="CorpusIndexInfoDto.FingerprintShort"/>.</summary>
    public const int FingerprintShortLength = 12;
}

/// <summary>One published corpus index.</summary>
public sealed class CorpusIndexInfoDto
{
    /// <summary>1 for the first published index, then one more per publish.</summary>
    public long Generation { get; init; }

    /// <summary>The HEAD read before the pass read the files; null outside a Git working tree.</summary>
    public string? HeadSha { get; init; }

    public string? Branch { get; init; }
    public DateTime IndexedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }

    /// <summary>The first 12 characters of the content fingerprint's SHA-256.</summary>
    public string? FingerprintShort { get; init; }

    /// <summary>The effective per-file size limit; null for a corpus without one.</summary>
    public int? MaxFileSizeKB { get; init; }

    /// <summary>At most <see cref="CorpusIndexLimits.MaxListedFiles"/> entries; <see cref="SkippedBySizeCount"/> is the full count.</summary>
    public IReadOnlyList<SkippedCorpusFileDto> SkippedBySize { get; init; } = Array.Empty<SkippedCorpusFileDto>();

    public int SkippedBySizeCount { get; init; }

    /// <summary>Files under a directory whose name starts with a dot, which no pass reads.</summary>
    public int SkippedDotDirectories { get; init; }

    /// <summary>At most <see cref="CorpusIndexLimits.MaxListedFiles"/> entries; <see cref="FailedFileCount"/> is the full count.</summary>
    public IReadOnlyList<FailedCorpusFileDto> FailedFiles { get; init; } = Array.Empty<FailedCorpusFileDto>();

    public int FailedFileCount { get; init; }
}

/// <summary>The part of a corpus status that every corpus shares; each service subclasses it.</summary>
public class CorpusIndexStatusDto
{
    public CorpusKind Kind { get; init; }

    /// <summary>The configuration key of the corpus folder, such as <c>WikiPath</c>; never its value.</summary>
    public string ConfigKey { get; init; } = string.Empty;

    /// <summary>Whether the configuration key is set.</summary>
    public bool Configured { get; init; }

    /// <summary>The folder the service reads, a built-in default included; null when there is none.</summary>
    public string? Path { get; init; }

    public bool PathExists { get; init; }

    /// <summary>Whether <c>&lt;Path&gt;\.git</c> is a directory; a worktree whose <c>.git</c> is a file reads false.</summary>
    public bool IsGitRepository { get; init; }

    /// <summary>The service's own flag: true once the first pass has ended.</summary>
    public bool IsIndexingComplete { get; init; }

    public string? CurrentHeadSha { get; init; }
    public string? CurrentBranch { get; init; }

    /// <summary>The published index; null until a pass has published one.</summary>
    public CorpusIndexInfoDto? Index { get; init; }

    /// <summary>
    /// For <see cref="CorpusRefreshMode.GitPoll"/>, the published index was read at the current HEAD;
    /// always true for <see cref="CorpusRefreshMode.StartupAndManual"/>.
    /// </summary>
    public bool UpToDate { get; init; }

    public CorpusRefreshMode RefreshMode { get; init; }

    /// <summary>The next periodic Git check; null for a corpus without one.</summary>
    public DateTime? NextCheckAtUtc { get; init; }

    public int? CheckIntervalMinutes { get; init; }
    public CorpusIndexJob? CurrentJob { get; init; }

    /// <summary>The most recent finished passes, newest first.</summary>
    public IReadOnlyList<CorpusIndexJob> RecentJobs { get; init; } = Array.Empty<CorpusIndexJob>();

    public DateTime ServerTimeUtc { get; init; }
}
