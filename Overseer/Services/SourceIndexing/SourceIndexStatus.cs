using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Overseer.Services.SourceIndexing;

/// <summary>The step a source indexing job is in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceIndexPhase
{
    Idle,
    WaitingForRepository,
    CheckingMakedefs,
    BuildingMakedefs,
    GeneratingHeaders,
    ReadingFiles,
    ParsingGameData,
    Publishing
}

/// <summary>What started a source indexing job.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceIndexTrigger
{
    Startup,
    RepositoryChange,
    Manual,
    FollowUp
}

/// <summary>An indexing pass, or a forced makedefs build followed by a pass when the headers changed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceIndexJobKind
{
    Index,
    BuildMakedefs
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceIndexJobOutcome
{
    Running,
    Succeeded,
    Failed,
    Canceled,
    SkippedNoChange
}

/// <summary>The answer to a request for a new job.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceIndexStartResult
{
    Started,
    AlreadyRunning,
    NotConfigured
}

/// <summary>A phase change reported by a step of a job.</summary>
public sealed record SourceIndexProgress(SourceIndexPhase Phase, string? Message = null);

/// <summary>One source indexing job, running or finished, as the status API reports it.</summary>
public sealed record SourceIndexJob
{
    public string Id { get; init; } = string.Empty;
    public SourceIndexTrigger Trigger { get; init; }
    public SourceIndexJobKind Kind { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? FinishedAtUtc { get; init; }
    public SourceIndexPhase Phase { get; init; }
    public int FilesRead { get; init; }

    /// <summary>The number of files the pass reads; null until the files are enumerated.</summary>
    public int? FilesTotal { get; init; }

    public SourceIndexJobOutcome Outcome { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> OutputTail { get; init; } = Array.Empty<string>();

    /// <summary>The name of the job's <see cref="MakedefsOutcomeKind"/>; null when the job did not check makedefs.</summary>
    public string? MakedefsOutcome { get; init; }
}

/// <summary>The state of one source service's index, its makedefs step and its jobs.</summary>
public sealed class SourceIndexStatusDto
{
    public bool Configured { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public bool PathExists { get; init; }
    public bool IsIndexingComplete { get; init; }
    public string? CurrentHeadSha { get; init; }
    public string? CurrentBranch { get; init; }

    /// <summary>The published index; null until a pass has published one.</summary>
    public SourceIndexInfoDto? Index { get; init; }

    /// <summary>An index exists and was read at the repository's current HEAD.</summary>
    public bool UpToDate { get; init; }

    public DateTime? NextCheckAtUtc { get; init; }
    public int CheckIntervalMinutes { get; init; }

    /// <summary>The makedefs outputs under <c>include/</c>; empty for a service without makedefs.</summary>
    public IReadOnlyList<GeneratedHeaderDto> GeneratedHeaders { get; init; } = Array.Empty<GeneratedHeaderDto>();

    public MakedefsStatusDto Makedefs { get; init; } = new();
    public SourceIndexJob? CurrentJob { get; init; }

    /// <summary>The most recent finished jobs, newest first.</summary>
    public IReadOnlyList<SourceIndexJob> RecentJobs { get; init; } = Array.Empty<SourceIndexJob>();
}

public sealed class SourceIndexInfoDto
{
    public int Generation { get; init; }
    public string? HeadSha { get; init; }
    public string? Branch { get; init; }
    public DateTime IndexedAtUtc { get; init; }
    public long DurationMs { get; init; }
    public int FileCount { get; init; }
    public long TotalLines { get; init; }
    public int ConstantCount { get; init; }
    public string? FingerprintShort { get; init; }
    public IReadOnlyList<SkippedFileDto> SkippedBySize { get; init; } = Array.Empty<SkippedFileDto>();
    public int SkippedExcludedDirectories { get; init; }
    public int MaxFileSizeKB { get; init; }
}

public sealed record SkippedFileDto(string Path, long Bytes);

public sealed record GeneratedHeaderDto(string Name, bool Exists, DateTime? LastWriteUtc, bool InIndex);

public sealed class MakedefsStatusDto
{
    public bool Supported { get; init; }
    public string? BuildCommand { get; init; }
    public bool BuildCommandToolExists { get; init; }
    public string? ExecutablePath { get; init; }
    public bool ExecutableExists { get; init; }
    public DateTime? ExecutableLastWriteUtc { get; init; }
    public string? BranchRestriction { get; init; }

    /// <summary><c>upToDate</c>, <c>stale</c> or <c>unknown</c>.</summary>
    public string InputsState { get; init; } = MakedefsInputsState.Unknown;

    public DateTime? StampGeneratedAtUtc { get; init; }

    /// <summary>The name of the last <see cref="MakedefsOutcomeKind"/>; null until a makedefs check has run.</summary>
    public string? LastOutcome { get; init; }

    public DateTime? LastBuildAtUtc { get; init; }
    public long? LastBuildDurationMs { get; init; }
    public IReadOnlyList<string> LastOutputTail { get; init; } = Array.Empty<string>();
}

/// <summary>The values of <see cref="MakedefsStatusDto.InputsState"/>.</summary>
public static class MakedefsInputsState
{
    public const string UpToDate = "upToDate";
    public const string Stale = "stale";
    public const string Unknown = "unknown";
}
