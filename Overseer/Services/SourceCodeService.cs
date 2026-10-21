using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Overseer.Services.SourceIndexing;

namespace Overseer.Services
{
    public class SourceCodeService : IHostedService, IDisposable
    {
        protected readonly ILogger _logger;
        protected readonly string _sourceCodePath;
        public string SourceCodePath => _sourceCodePath;
        private readonly string _configPathKey;
        private readonly int _maxFileSizeKB;
        private Timer? _reindexTimer;

        public class ConstantInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int LineNumber { get; set; }
        }

        private readonly HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "vis_tab.c", "vis_tab.h", "date.h"
        };

        private readonly Dictionary<string, string> _flagDescriptions = new(StringComparer.OrdinalIgnoreCase);

        /* The published index: documents, constants, the game-data parser and the item resolver of
           one completed pass. A pass builds a new snapshot and replaces this reference; every query
           reads it once and uses that snapshot to the end. */
        private SourceIndexSnapshot _snapshot = SourceIndexSnapshot.Empty;

        public GameDataParser Parser => Volatile.Read(ref _snapshot).Parser;

        /* The character tables of each pass, keyed by that pass's parser, so they are published and
           replaced together with its snapshot. */
        private readonly ConditionalWeakTable<GameDataParser, CharacterTables> _characterTables = new();

        private readonly IConfiguration _configuration;
        private readonly IExternalProcessRunner _processRunner;
        private MakedefsHeaderGenerator? _makedefs;
        private readonly int _maxFunctionBodyLines;
        private volatile bool _isIndexingComplete = false;

        /// <summary>True once the first pass has ended, published or not; never returns to false.</summary>
        public bool IsIndexingComplete => _isIndexingComplete;

        /// <summary>The first pass, started by <see cref="StartAsync"/>. Never faults.</summary>
        public Task InitializationTask { get; private set; } = Task.CompletedTask;

        /// <summary>The published snapshot's generation: 0 before the first publish, then one more per publish.</summary>
        public int IndexGeneration => Volatile.Read(ref _snapshot).Generation;

        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromMinutes(10);
        private const int MaxRecentJobs = 20;

        /* One pass at a time: start-up, the HEAD poll and manual requests all take this gate. A
           request that finds it taken sets _pendingFollowUp (automatic triggers only), so at most
           one follow-up pass is ever queued. */
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _pendingFollowUp;
        private readonly object _jobLock = new();
        private RunningJob? _currentJob;
        private CancellationTokenSource? _currentJobCts;
        private string? _lastStartedJobId;
        private readonly LinkedList<SourceIndexJob> _recentJobs = new();
        private DateTime? _nextCheckAtUtc;
        private Task _runningPass = Task.CompletedTask;
        private int _jobCounter;
        private volatile bool _stopRequested;
        private bool _disposed;

        /// <summary>Per-match timeout for a regular expression a model supplies.</summary>
        internal static readonly TimeSpan UserRegexMatchTimeout = TimeSpan.FromMilliseconds(200);

        /// <summary>Total time a model-supplied regular expression may spend across one search.</summary>
        internal static readonly TimeSpan UserRegexSearchDeadline = TimeSpan.FromSeconds(5);

        internal const string RegexTooSlowMessage =
            "Error: The regular expression took too long to evaluate and was stopped. Use a simpler pattern, or search for a literal string.";

        /// <summary>What the index the tools currently read holds, generated headers included; null until the first pass publishes.</summary>
        public CorpusContentFingerprint? ContentFingerprint => Volatile.Read(ref _snapshot).Fingerprint;

        protected virtual string[] TargetDirectories => new[] { "src", "include", "dat", @"win\win32\xpl" };

        /// <summary>Whether a pass generates the headers with makedefs before reading the files.</summary>
        protected virtual bool SupportsMakedefs => true;

        /// <summary>How far apart two equal HEAD reads must be for the repository to count as quiet.</summary>
        protected virtual TimeSpan QuietPeriod => TimeSpan.FromSeconds(3);

        /// <summary>How long a pass waits for a quiet repository before it reads the files anyway.</summary>
        protected virtual TimeSpan QuietWaitLimit => TimeSpan.FromMinutes(2);

        /// <summary>How often a pass looks again while <c>.git/index.lock</c> exists.</summary>
        protected virtual TimeSpan LockPollInterval => TimeSpan.FromMilliseconds(500);

        public SourceCodeService(IConfiguration configuration, ILogger<SourceCodeService> logger, IExternalProcessRunner? processRunner = null)
            : this(configuration, (ILogger)logger, "SourceCodePath", processRunner)
        {
        }

        protected SourceCodeService(IConfiguration configuration, ILogger logger, string configPathKey, IExternalProcessRunner? processRunner = null)
        {
            _configuration = configuration;
            _logger = logger;
            _processRunner = processRunner ?? new ExternalProcessRunner();
            _configPathKey = configPathKey;
            _sourceCodePath = configuration[configPathKey] ?? string.Empty;

            if (!int.TryParse(configuration["MaxSourceFileSizeKB"], out _maxFileSizeKB))
            {
                _maxFileSizeKB = 800;
            }
            _maxFunctionBodyLines = configuration.GetValue<int>("Tools:get_function_definition:MaxLinesPerChunk", 150);
        }

        /// <summary>
        /// Loads the flag descriptions, starts the first pass in the background and the 10-minute HEAD
        /// poll, and returns at once.
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("{Service} starting. Path: {Path}", ServiceName, _sourceCodePath);
            LoadFlagDescriptions();
            InitializationTask = TryStartPass(SourceIndexTrigger.Startup, SourceIndexJobKind.Index, forceMakedefs: false, queueIfBusy: false)
                ?? Task.CompletedTask;

            // Check for changes every 10 minutes
            lock (_jobLock)
            {
                _nextCheckAtUtc = DateTime.UtcNow + UpdateCheckInterval;
            }
            _reindexTimer = new Timer(CheckForUpdates, null, UpdateCheckInterval, UpdateCheckInterval);

            return Task.CompletedTask;
        }

        protected virtual void LoadFlagDescriptions()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "flag_descriptions.json");
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            _flagDescriptions[kvp.Key] = kvp.Value;
                        }
                    }
                    _logger.LogInformation("Loaded {Count} flag descriptions.", _flagDescriptions.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading flag descriptions.");
            }
        }

        /// <summary>Cancels the running job, stops the poll and waits for the pass to end or for <paramref name="cancellationToken"/>.</summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _stopRequested = true;
            try
            {
                _reindexTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                /* Already disposed. */
            }
            CancelCurrentJob();

            try
            {
                await Volatile.Read(ref _runningPass).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                /* The host's shutdown timeout fired first. */
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopRequested = true;
            _reindexTimer?.Dispose();
            CancelCurrentJob();

            /* A canceled pass ends within moments; the gate is disposed only once nothing can release it. */
            bool ended;
            try
            {
                ended = Volatile.Read(ref _runningPass).Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                ended = true;
            }
            if (ended) _gate.Dispose();
        }

        private string ServiceName => GetType().Name;

        private bool IsPathAvailable => !string.IsNullOrWhiteSpace(_sourceCodePath) && Directory.Exists(_sourceCodePath);

        /// <summary>The id of the running job, or null.</summary>
        public string? CurrentJobId
        {
            get { lock (_jobLock) return _currentJob?.Id; }
        }

        /// <summary>The id of the most recently started job, running or finished; null before the first.</summary>
        public string? LastStartedJobId
        {
            get { lock (_jobLock) return _lastStartedJobId; }
        }

        /// <summary>The outcome of the last makedefs check; null until one has run, and always null without makedefs.</summary>
        public MakedefsOutcome? LastMakedefsOutcome => SupportsMakedefs ? Volatile.Read(ref _makedefs)?.LastOutcome : null;

        private MakedefsHeaderGenerator MakedefsGenerator =>
            LazyInitializer.EnsureInitialized(ref _makedefs, () => new MakedefsHeaderGenerator(_sourceCodePath, _configuration, _processRunner, _logger));

        /// <summary>
        /// Starts an indexing pass. <see cref="SourceIndexStartResult.AlreadyRunning"/> when a job runs;
        /// an automatic trigger then queues one follow-up pass, a manual one is refused.
        /// </summary>
        public SourceIndexStartResult RequestReindex(SourceIndexTrigger trigger)
        {
            if (!IsPathAvailable) return SourceIndexStartResult.NotConfigured;
            var pass = TryStartPass(trigger, SourceIndexJobKind.Index, forceMakedefs: false, queueIfBusy: trigger != SourceIndexTrigger.Manual);
            return pass != null ? SourceIndexStartResult.Started : SourceIndexStartResult.AlreadyRunning;
        }

        /// <summary>
        /// Builds and runs makedefs even when its inputs are unchanged, then re-indexes when a generated
        /// header changed or no index exists yet.
        /// </summary>
        public SourceIndexStartResult RequestMakedefsBuild()
        {
            if (!SupportsMakedefs || !IsPathAvailable) return SourceIndexStartResult.NotConfigured;
            var pass = TryStartPass(SourceIndexTrigger.Manual, SourceIndexJobKind.BuildMakedefs, forceMakedefs: true, queueIfBusy: false);
            return pass != null ? SourceIndexStartResult.Started : SourceIndexStartResult.AlreadyRunning;
        }

        /// <summary>Requests cancellation of the running job; false when none runs. The previous index keeps serving.</summary>
        public bool CancelCurrentJob()
        {
            CancellationTokenSource? cts;
            lock (_jobLock)
            {
                cts = _currentJob != null ? _currentJobCts : null;
            }
            if (cts == null) return false;

            /* Callbacks run on the thread pool, never inline on the caller's thread. */
            _ = cts.CancelAsync();
            return true;
        }

        /// <summary>
        /// The names of the makedefs outputs missing under <c>include/</c>; empty without makedefs or
        /// when the source path is not configured or missing.
        /// </summary>
        public IReadOnlyList<string> GetMissingGeneratedHeaders()
        {
            if (!SupportsMakedefs || !IsPathAvailable) return Array.Empty<string>();
            return MakedefsInputSet.GeneratedHeaders
                .Where(name => !File.Exists(Path.Combine(_sourceCodePath, "include", name)))
                .ToList();
        }

        /// <summary>The configuration, the published index, makedefs and the jobs, for the dashboard.</summary>
        public SourceIndexStatusDto GetStatus()
        {
            var index = Volatile.Read(ref _snapshot);
            bool configured = !string.IsNullOrWhiteSpace(_sourceCodePath);
            bool exists = configured && Directory.Exists(_sourceCodePath);
            string? currentHead = exists ? GitHelper.GetGitHeadSha(_sourceCodePath) : null;
            string? currentBranch = exists ? NullIfEmpty(GitHelper.GetCurrentBranch(_sourceCodePath)) : null;

            SourceIndexInfoDto? info = null;
            if (index.Generation > 0)
            {
                string? fingerprint = index.Fingerprint?.Sha256;
                info = new SourceIndexInfoDto
                {
                    Generation = index.Generation,
                    HeadSha = index.HeadSha,
                    Branch = index.Branch,
                    IndexedAtUtc = index.IndexedAtUtc,
                    DurationMs = (long)index.Duration.TotalMilliseconds,
                    FileCount = index.Documents.Count,
                    TotalLines = index.TotalLines,
                    ConstantCount = index.Constants.Count,
                    FingerprintShort = fingerprint == null ? null : fingerprint.Substring(0, Math.Min(12, fingerprint.Length)),
                    SkippedBySize = index.SkippedBySize.Select(s => new SkippedFileDto(s.Path, s.Bytes)).ToList(),
                    SkippedExcludedDirectories = index.SkippedExcludedDirectories,
                    MaxFileSizeKB = _maxFileSizeKB
                };
            }

            var headers = new List<GeneratedHeaderDto>();
            if (SupportsMakedefs && exists)
            {
                foreach (string name in MakedefsInputSet.GeneratedHeaders)
                {
                    string path = Path.Combine(_sourceCodePath, "include", name);
                    bool headerExists = File.Exists(path);
                    headers.Add(new GeneratedHeaderDto(
                        name,
                        headerExists,
                        headerExists ? File.GetLastWriteTimeUtc(path) : null,
                        index.Documents.ContainsKey("include/" + name)));
                }
            }

            SourceIndexJob? currentJob;
            List<SourceIndexJob> recentJobs;
            DateTime? nextCheck;
            lock (_jobLock)
            {
                currentJob = _currentJob?.ToJob(null);
                recentJobs = _recentJobs.ToList();
                nextCheck = _nextCheckAtUtc;
            }

            return new SourceIndexStatusDto
            {
                Configured = configured,
                SourcePath = _sourceCodePath,
                PathExists = exists,
                IsIndexingComplete = _isIndexingComplete,
                CurrentHeadSha = currentHead,
                CurrentBranch = currentBranch,
                Index = info,
                UpToDate = info != null && string.Equals(index.HeadSha, currentHead, StringComparison.Ordinal),
                NextCheckAtUtc = nextCheck,
                CheckIntervalMinutes = (int)UpdateCheckInterval.TotalMinutes,
                GeneratedHeaders = headers,
                Makedefs = BuildMakedefsStatus(),
                CurrentJob = currentJob,
                RecentJobs = recentJobs
            };
        }

        private MakedefsStatusDto BuildMakedefsStatus()
        {
            if (!SupportsMakedefs) return new MakedefsStatusDto { Supported = false };

            var state = MakedefsGenerator.GetState();
            return new MakedefsStatusDto
            {
                Supported = true,
                BuildCommand = NullIfEmpty(state.BuildCommand),
                BuildCommandToolExists = state.BuildCommandToolExists,
                ExecutablePath = state.ExecutablePath,
                ExecutableExists = state.ExecutableExists,
                ExecutableLastWriteUtc = state.ExecutableLastWriteUtc,
                BranchRestriction = state.BranchRestriction,
                InputsState = state.InputsState,
                StampGeneratedAtUtc = state.StampGeneratedAtUtc,
                LastOutcome = state.LastOutcome?.Kind.ToString(),
                LastBuildAtUtc = state.LastBuildAtUtc,
                LastBuildDurationMs = state.LastBuildDuration.HasValue ? (long)state.LastBuildDuration.Value.TotalMilliseconds : null,
                LastOutputTail = state.LastOutcome?.OutputTail ?? Array.Empty<string>()
            };
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private void CheckForUpdates(object? state)
        {
            try
            {
                lock (_jobLock)
                {
                    _nextCheckAtUtc = DateTime.UtcNow + UpdateCheckInterval;
                }
                if (_stopRequested || !IsPathAvailable) return;

                string? currentSha = GitHelper.GetGitHeadSha(_sourceCodePath);
                string? indexedSha = Volatile.Read(ref _snapshot).HeadSha;
                if (!string.IsNullOrEmpty(currentSha) && currentSha != indexedSha)
                {
                    _logger.LogInformation("Repository update detected ({OldSha} -> {NewSha}). Re-indexing.", indexedSha, currentSha);
                    RequestReindex(SourceIndexTrigger.RepositoryChange);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for repository updates.");
            }
        }

        /// <summary>
        /// Takes the gate and starts a job on the thread pool; null when the gate is taken (setting the
        /// follow-up flag when <paramref name="queueIfBusy"/>) or the service is stopping.
        /// </summary>
        private Task? TryStartPass(SourceIndexTrigger trigger, SourceIndexJobKind kind, bool forceMakedefs, bool queueIfBusy)
        {
            if (_stopRequested) return null;
            try
            {
                if (!_gate.Wait(0))
                {
                    if (queueIfBusy) Interlocked.Exchange(ref _pendingFollowUp, 1);
                    return null;
                }
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            string prefix = kind == SourceIndexJobKind.BuildMakedefs ? "makedefs" : "index";
            var job = new RunningJob($"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Interlocked.Increment(ref _jobCounter)}", trigger, kind);

            /* Holds no timer or wait handle, so it is left to the collector rather than disposed
               while a CancelAsync may still be running on it. */
            var cts = new CancellationTokenSource();
            lock (_jobLock)
            {
                _currentJob = job;
                _currentJobCts = cts;
                _lastStartedJobId = job.Id;
            }
            _logger.LogInformation("{Service}: {Kind} job {Id} started ({Trigger}).", ServiceName, kind, job.Id, trigger);

            /* Recorded before the job runs, so a follow-up the job starts always replaces it. */
            var outer = new Task<Task>(() => RunPassAsync(job, forceMakedefs, cts.Token));
            var pass = outer.Unwrap();
            Volatile.Write(ref _runningPass, pass);
            outer.Start(TaskScheduler.Default);
            return pass;
        }

        /// <summary>Runs one job and records its outcome; never throws. Releases the gate and starts a queued follow-up.</summary>
        private async Task RunPassAsync(RunningJob job, bool forceMakedefs, CancellationToken ct)
        {
            try
            {
                await RunPipelineAsync(job, forceMakedefs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("{Service}: job {Id} was canceled; the previous index keeps serving.", ServiceName, job.Id);
                job.Complete(SourceIndexJobOutcome.Canceled, "Canceled; the previous index keeps serving.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing source repository.");
                job.Complete(SourceIndexJobOutcome.Failed, $"{ex.Message} The previous index keeps serving.");
            }
            finally
            {
                _isIndexingComplete = true;
                FinishJob(job);
                try
                {
                    _gate.Release();
                }
                catch (ObjectDisposedException)
                {
                    /* Disposed while this pass was ending. */
                }

                if (Interlocked.Exchange(ref _pendingFollowUp, 0) == 1 && !_stopRequested)
                {
                    _logger.LogInformation("{Service}: starting the queued follow-up pass.", ServiceName);
                    _ = TryStartPass(SourceIndexTrigger.FollowUp, SourceIndexJobKind.Index, forceMakedefs: false, queueIfBusy: true);
                }
            }
        }

        private void FinishJob(RunningJob job)
        {
            var finished = job.ToJob(DateTime.UtcNow);
            lock (_jobLock)
            {
                _recentJobs.AddFirst(finished);
                while (_recentJobs.Count > MaxRecentJobs) _recentJobs.RemoveLast();
                if (ReferenceEquals(_currentJob, job))
                {
                    _currentJob = null;
                    _currentJobCts = null;
                }
            }
            _logger.LogInformation("{Service}: job {Id} ended: {Outcome}. {Message}", ServiceName, job.Id, finished.Outcome, finished.Message);
        }

        private void SetPhase(RunningJob job, SourceIndexPhase phase)
        {
            if (job.Phase == phase) return;
            job.Phase = phase;
            _logger.LogInformation("{Service}: job {Id} phase {Phase}.", ServiceName, job.Id, phase);
        }

        /// <summary>
        /// The only code that builds an index: waits for a quiet repository, generates the headers,
        /// reads the files and parses the game data into a new snapshot, and publishes it with one
        /// reference assignment. Anything that throws publishes nothing.
        /// </summary>
        private async Task RunPipelineAsync(RunningJob job, bool forceMakedefs, CancellationToken ct)
        {
            string root = _sourceCodePath;
            if (!IsPathAvailable)
            {
                /* The empty snapshot stays published, as an unconfigured service has nothing to index. */
                _logger.LogWarning("Source code repository not found at {Path}", root);
                job.Complete(SourceIndexJobOutcome.Failed, string.IsNullOrWhiteSpace(root)
                    ? $"{_configPathKey} is not configured."
                    : $"The source path {root} does not exist.");
                return;
            }

            SetPhase(job, SourceIndexPhase.WaitingForRepository);
            string? headBefore = await WaitForQuietRepositoryAsync(root, ct).ConfigureAwait(false);
            string? branch = NullIfEmpty(GitHelper.GetCurrentBranch(root));
            var published = Volatile.Read(ref _snapshot);

            if (job.Kind == SourceIndexJobKind.Index
                && job.Trigger is SourceIndexTrigger.RepositoryChange or SourceIndexTrigger.FollowUp
                && published.Generation > 0
                && headBefore != null
                && string.Equals(headBefore, published.HeadSha, StringComparison.Ordinal))
            {
                job.Complete(SourceIndexJobOutcome.SkippedNoChange, $"HEAD {ShortSha(headBefore)} is already indexed.");
                return;
            }

            MakedefsOutcome? makedefs = null;
            string? makedefsError = null;
            if (SupportsMakedefs)
            {
                SetPhase(job, SourceIndexPhase.CheckingMakedefs);
                try
                {
                    var progress = new InlineProgress(p => SetPhase(job, p.Phase));
                    makedefs = await MakedefsGenerator.EnsureHeadersAsync(forceMakedefs, progress, ct).ConfigureAwait(false);
                    job.SetMakedefs(makedefs);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    /* A makedefs failure never stops indexing: the headers on disk are indexed as they are. */
                    _logger.LogError(ex, "makedefs header regeneration failed. Continuing with existing headers (if any).");
                    makedefsError = $"The makedefs check failed: {ex.Message}";
                }
            }

            string makedefsSummary = makedefs?.Message ?? makedefsError ?? string.Empty;
            if (job.Kind == SourceIndexJobKind.BuildMakedefs
                && makedefs?.HeadersChanged != true
                && published.Generation > 0)
            {
                bool built = makedefs?.HeadersCurrent == true;
                job.Complete(built ? SourceIndexJobOutcome.Succeeded : SourceIndexJobOutcome.Failed,
                    (makedefsSummary + " The index was kept.").Trim());
                return;
            }

            await OnBeforeReadFilesAsync(job.Trigger, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            SetPhase(job, SourceIndexPhase.ReadingFiles);
            var indexStopwatch = Stopwatch.StartNew();
            var read = ReadFiles(root, job, ct);

            SetPhase(job, SourceIndexPhase.ParsingGameData);
            var parser = new GameDataParser();
            var itemResolver = new ObjectsMacroResolver(parser);
            ParseGameData(read.Documents, parser, itemResolver);
            ct.ThrowIfCancellationRequested();

            SetPhase(job, SourceIndexPhase.Publishing);
            var fingerprintBuilder = new CorpusContentFingerprintBuilder();
            foreach (var doc in read.Documents.Values)
            {
                fingerprintBuilder.Add(doc.RelativePath, string.Join("\n", doc.ContentLines));
            }
            var fingerprint = fingerprintBuilder.Build();

            var snapshot = new SourceIndexSnapshot
            {
                Documents = read.Documents,
                Constants = read.Constants,
                Parser = parser,
                ItemResolver = itemResolver,
                Fingerprint = fingerprint,
                HeadSha = headBefore,
                Branch = branch,
                IndexedAtUtc = fingerprint.IndexedAtUtc,
                Duration = indexStopwatch.Elapsed,
                Generation = published.Generation + 1,
                TotalLines = read.TotalLines,
                SkippedBySize = read.SkippedBySize,
                SkippedExcludedDirectories = read.SkippedExcludedDirectories
            };

            ct.ThrowIfCancellationRequested();
            Volatile.Write(ref _snapshot, snapshot);
            _isIndexingComplete = true;

            _logger.LogInformation("Indexed {Count} source files.", read.Documents.Count);
            if (read.SkippedExcludedDirectories > 0)
            {
                _logger.LogInformation("Skipped {Count} source file(s) under dot-, bin or obj directories.", read.SkippedExcludedDirectories);
            }

            /* The files were read at headBefore; a newer HEAD gets its own pass. */
            string? headAfter = GitHelper.GetGitHeadSha(root);
            if (!string.Equals(headAfter, headBefore, StringComparison.Ordinal))
            {
                _logger.LogInformation("{Service}: HEAD moved during the pass ({Before} -> {After}); a follow-up pass is queued.",
                    ServiceName, headBefore, headAfter);
                Interlocked.Exchange(ref _pendingFollowUp, 1);
            }

            job.Complete(SourceIndexJobOutcome.Succeeded,
                ($"Indexed {read.Documents.Count} files (generation {snapshot.Generation}). " + makedefsSummary).Trim());
        }

        /// <summary>
        /// Runs inside every indexing pass, after the makedefs step and before the files are read.
        /// Cancellation and exceptions end the pass without publishing.
        /// </summary>
        protected virtual Task OnBeforeReadFilesAsync(SourceIndexTrigger trigger, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Waits until <c>.git/index.lock</c> is absent and HEAD reads the same twice
        /// <see cref="QuietPeriod"/> apart, at most <see cref="QuietWaitLimit"/>; returns that HEAD.
        /// Outside a Git working tree it returns at once.
        /// </summary>
        private async Task<string?> WaitForQuietRepositoryAsync(string root, CancellationToken ct)
        {
            string gitDir = Path.Combine(root, ".git");
            if (!Directory.Exists(gitDir)) return GitHelper.GetGitHeadSha(root);

            var stopwatch = Stopwatch.StartNew();
            string? previous = null;
            bool havePrevious = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                bool locked = File.Exists(Path.Combine(gitDir, "index.lock"));
                string? head = GitHelper.GetGitHeadSha(root);
                if (!locked && havePrevious && string.Equals(head, previous, StringComparison.Ordinal))
                {
                    return head;
                }

                if (stopwatch.Elapsed >= QuietWaitLimit)
                {
                    _logger.LogWarning("{Service}: the repository at {Path} did not become quiet within {Limit}; indexing anyway.",
                        ServiceName, root, QuietWaitLimit);
                    return head;
                }

                previous = head;
                havePrevious = !locked;
                await Task.Delay(locked ? LockPollInterval : QuietPeriod, ct).ConfigureAwait(false);
            }
        }

        private static string ShortSha(string sha) => sha.Length > 12 ? sha.Substring(0, 12) : sha;

        /// <summary>Reports a progress value on the reporting thread, so a phase change is visible at once.</summary>
        private sealed class InlineProgress : IProgress<SourceIndexProgress>
        {
            private readonly Action<SourceIndexProgress> _report;

            public InlineProgress(Action<SourceIndexProgress> report)
            {
                _report = report;
            }

            public void Report(SourceIndexProgress value) => _report(value);
        }

        /// <summary>The mutable record of the running job; <see cref="ToJob"/> copies it for the status API.</summary>
        private sealed class RunningJob
        {
            private readonly object _lock = new();
            private volatile SourceIndexPhase _phase = SourceIndexPhase.Idle;
            private int _filesTotal = -1;
            private SourceIndexJobOutcome _outcome = SourceIndexJobOutcome.Running;
            private string? _message;
            private MakedefsOutcome? _makedefs;

            /// <summary>Incremented with <see cref="Interlocked"/> per file read.</summary>
            public int FilesRead;

            public RunningJob(string id, SourceIndexTrigger trigger, SourceIndexJobKind kind)
            {
                Id = id;
                Trigger = trigger;
                Kind = kind;
                StartedAtUtc = DateTime.UtcNow;
            }

            public string Id { get; }
            public SourceIndexTrigger Trigger { get; }
            public SourceIndexJobKind Kind { get; }
            public DateTime StartedAtUtc { get; }

            public SourceIndexPhase Phase
            {
                get => _phase;
                set => _phase = value;
            }

            public int FilesTotal
            {
                set => Volatile.Write(ref _filesTotal, value);
            }

            public void SetMakedefs(MakedefsOutcome outcome)
            {
                lock (_lock) _makedefs = outcome;
            }

            /// <summary>Records the outcome; the first completion wins.</summary>
            public void Complete(SourceIndexJobOutcome outcome, string? message)
            {
                lock (_lock)
                {
                    if (_outcome != SourceIndexJobOutcome.Running) return;
                    _outcome = outcome;
                    _message = message;
                }
            }

            public SourceIndexJob ToJob(DateTime? finishedAtUtc)
            {
                int filesTotal = Volatile.Read(ref _filesTotal);
                lock (_lock)
                {
                    var outcome = _outcome;
                    if (finishedAtUtc.HasValue && outcome == SourceIndexJobOutcome.Running)
                    {
                        outcome = SourceIndexJobOutcome.Failed;
                    }

                    return new SourceIndexJob
                    {
                        Id = Id,
                        Trigger = Trigger,
                        Kind = Kind,
                        StartedAtUtc = StartedAtUtc,
                        FinishedAtUtc = finishedAtUtc,
                        Phase = _phase,
                        FilesRead = Volatile.Read(ref FilesRead),
                        FilesTotal = filesTotal < 0 ? null : filesTotal,
                        Outcome = outcome,
                        Message = _message,
                        OutputTail = _makedefs?.OutputTail ?? Array.Empty<string>(),
                        MakedefsOutcome = _makedefs?.Kind.ToString()
                    };
                }
            }
        }

        /// <summary>
        /// Waits until no job runs and no follow-up is queued; false when <paramref name="timeout"/>
        /// passes first. For tests.
        /// </summary>
        internal async Task<bool> WaitForIdleAsync(TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                var running = Volatile.Read(ref _runningPass);
                var remaining = timeout - stopwatch.Elapsed;
                try
                {
                    await running.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return false;
                }

                bool idle;
                lock (_jobLock)
                {
                    idle = _currentJob == null;
                }
                if (idle && Volatile.Read(ref _pendingFollowUp) == 0 && ReferenceEquals(running, Volatile.Read(ref _runningPass)))
                {
                    return true;
                }
                await Task.Delay(5).ConfigureAwait(false);
            }
            return false;
        }

        /// <summary>What one pass read from disk, before it becomes a snapshot.</summary>
        private sealed record IndexReadResult(
            Dictionary<string, SourceDocument> Documents,
            Dictionary<string, ConstantInfo> Constants,
            long TotalLines,
            IReadOnlyList<(string Path, long Bytes)> SkippedBySize,
            int SkippedExcludedDirectories);

        /// <summary>
        /// Reads every indexed file under the target directories into new collections and parses its
        /// constants; the published snapshot is not touched. Checks <paramref name="ct"/> per file.
        /// </summary>
        private IndexReadResult ReadFiles(string root, RunningJob job, CancellationToken ct)
        {
            var newDocuments = new Dictionary<string, SourceDocument>(StringComparer.OrdinalIgnoreCase);
            var newConstants = new Dictionary<string, ConstantInfo>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<(string File, string RelativePath, bool IsNetCode)>();
            var skippedBySize = new List<(string Path, long Bytes)>();
            int skippedExcludedFiles = 0;

            foreach (var dir in TargetDirectories)
            {
                string fullDirPath = Path.Combine(root, dir);
                if (!Directory.Exists(fullDirPath)) continue;

                var files = Directory.GetFiles(fullDirPath, "*.*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    string relPath = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (IsUnderExcludedDirectory(relPath))
                    {
                        skippedExcludedFiles++;
                        continue;
                    }

                    var fileInfo = new FileInfo(file);
                    string ext = fileInfo.Extension.ToLowerInvariant();
                    string fileName = fileInfo.Name;

                    // Check exclusions
                    if (_excludedFiles.Contains(fileName)) continue;
                    if (fileName.EndsWith("conf.h", StringComparison.OrdinalIgnoreCase) ||
                        // Rough approximation of platform headers, avoiding false positives if possible
                        fileName.StartsWith("win", StringComparison.OrdinalIgnoreCase) && ext == ".h"
                            && !string.Equals(fileName, "wintype.h", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(fileName, "winprocs.h", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("mac", StringComparison.OrdinalIgnoreCase) && ext == ".h" ||
                        fileName.StartsWith("qt", StringComparison.OrdinalIgnoreCase) && ext == ".h")
                    {
                        // Skip platform specific headers
                        continue;
                    }

                    // Check extensions
                    bool isNormalMode = ext == ".c" || ext == ".h" || ext == ".des" || ext == ".txt";
                    bool isDebugMode = ext == ".cs" || ext == ".xaml";

                    if (!isNormalMode && !isDebugMode) continue;

                    if (fileInfo.Length <= _maxFileSizeKB * 1024)
                    {
                        candidates.Add((file, relPath, isDebugMode));
                    }
                    else
                    {
                        skippedBySize.Add((relPath, fileInfo.Length));
                    }
                }
            }

            job.FilesTotal = candidates.Count;
            long totalLines = 0;

            foreach (var (file, relPath, isNetCode) in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var contentLines = File.ReadAllLines(file);
                newDocuments[relPath] = new SourceDocument
                {
                    FilePath = file,
                    RelativePath = relPath,
                    IsNetCode = isNetCode,
                    ContentLines = contentLines
                };
                totalLines += contentLines.Length;

                ParseConstants(contentLines, relPath, newConstants);
                Interlocked.Increment(ref job.FilesRead);
            }

            return new IndexReadResult(newDocuments, newConstants, totalLines, skippedBySize, skippedExcludedFiles);
        }

        /// <summary>The <c>#define</c> and enum constants of one file, added to <paramref name="constants"/>.</summary>
        private static void ParseConstants(string[] contentLines, string relPath, Dictionary<string, ConstantInfo> constants)
        {
            bool inEnum = false;
            // Set by an "enum name" line without a brace; the next non-blank line opens the enum if it starts with "{".
            // A line with "(" is a function signature or prototype taking an enum parameter, never an enum header.
            bool pendingEnum = false;
            for (int i = 0; i < contentLines.Length; i++)
            {
                string line = contentLines[i].Trim();

                if (pendingEnum && line.Length > 0)
                {
                    pendingEnum = false;
                    if (line.StartsWith("{", StringComparison.Ordinal)) inEnum = true;
                }

                var defineMatch = Regex.Match(line, @"^#define\s+([A-Za-z0-9_]+)(?:\s+([^/]*))?");
                if (defineMatch.Success)
                {
                    string name = defineMatch.Groups[1].Value;
                    string val = defineMatch.Groups[2].Success ? defineMatch.Groups[2].Value.Trim() : "";
                    constants[name] = new ConstantInfo { Name = name, Value = val, FilePath = relPath, LineNumber = i + 1 };
                    continue;
                }

                if (Regex.IsMatch(line, @"\benum\b.*\{")) inEnum = true;
                else if (!inEnum && !line.StartsWith("#", StringComparison.Ordinal)
                    && !line.Contains('{') && !line.Contains(';') && !line.Contains('(')
                    && Regex.IsMatch(line, @"\benum\b"))
                {
                    pendingEnum = true;
                }

                if (inEnum)
                {
                    var matches = Regex.Matches(line, @"([A-Za-z0-9_]+)\s*(?:=\s*([^,}]+))?\s*(?:,|})");
                    foreach (Match m in matches)
                    {
                        string name = m.Groups[1].Value;
                        if (name == "enum") continue;
                        string val = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                        constants[name] = new ConstantInfo { Name = name, Value = val, FilePath = relPath, LineNumber = i + 1 };
                    }
                    if (line.Contains("}")) inEnum = false;
                }
            }
        }

        /// <summary>
        /// True when any segment of a repository-relative path begins with a dot, or equals
        /// "bin" or "obj" — build output and IDE caches are never game source.
        /// </summary>
        private static bool IsUnderExcludedDirectory(string relativePath)
        {
            return relativePath
                .Split('/')
                .Any(segment => segment.StartsWith(".", StringComparison.Ordinal)
                    || string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Parses the game data of one pass's <paramref name="documents"/> into that pass's new
        /// <paramref name="parser"/> and <paramref name="itemResolver"/>, which no query sees until the
        /// snapshot holding them is published.
        /// </summary>
        private protected virtual void ParseGameData(IReadOnlyDictionary<string, SourceDocument> documents, GameDataParser parser, ObjectsMacroResolver itemResolver)
        {
            try
            {
                var permonstDoc = documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\permonst.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/permonst.h", StringComparison.OrdinalIgnoreCase));
                var objclassDoc = documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\objclass.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/objclass.h", StringComparison.OrdinalIgnoreCase));
                if (permonstDoc != null && objclassDoc != null)
                {
                    parser.ParseStructs(permonstDoc.ContentLines, objclassDoc.ContentLines);
                }
                var monstDoc = documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\monst.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/monst.c", StringComparison.OrdinalIgnoreCase));
                var objectsDoc = documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\objects.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/objects.c", StringComparison.OrdinalIgnoreCase));
                if (monstDoc != null) parser.ParseMacros(monstDoc.ContentLines);
                if (objectsDoc != null) parser.ParseMacros(objectsDoc.ContentLines);

                /* objclass.h supplies the enum constants the ternary conditions in CHARGEDRING,
                   MISCELLANEOUSITEM, GENERAL_TOOL, GENERAL_SPELLTOOL, CONTAINER and GENERAL_ROCK
                   compare against; without it roughly sixty entries return C ternary text in place
                   of a value. NetHackSourceCodeService overrides this method with a no-op, so the
                   resolver is GnollHack-only without a guard here. */
                if (objectsDoc != null)
                {
                    itemResolver.Load(objectsDoc.ContentLines, objclassDoc?.ContentLines);
                    _logger.LogInformation(
                        "Indexed {Count} objects.c item entries for structured item stats.",
                        itemResolver.ItemNames.Count);
                }

                _logger.LogInformation("Parsed game data macros and structs.");
            }
            catch (Exception pex)
            {
                _logger.LogError(pex, "Error parsing game data macros and structs.");
            }

            try
            {
                string[]? Lines(string path) => documents.TryGetValue(path, out var d) ? d.ContentLines : null;
                var tables = CharacterTables.Parse(Lines("src/role.c"), Lines("src/attrib.c"), Lines("src/prop.c"), Lines("include/artilist.h"));
                _characterTables.AddOrUpdate(parser, tables);
                _logger.LogInformation("Parsed {RoleCount} roles and {RaceCount} races for character info.", tables.Roles.Count, tables.Races.Count);
                if (tables.Problems.Count > 0)
                {
                    _logger.LogInformation("Character tables: {Problems}", string.Join(" ", tables.Problems));
                }
            }
            catch (Exception cex)
            {
                _logger.LogError(cex, "Error parsing the character tables.");
            }
        }

        public string SearchFiles(string query, string? fileFilter, int maxResults, bool includeNetCode, int maxResultLength, bool isRegex = false, bool filenamesOnly = false, int contextLines = 5, bool caseSensitive = false)
        {
            maxResults = Math.Clamp(maxResults, 1, 100);
            contextLines = Math.Clamp(contextLines, 0, 25);
            if (string.IsNullOrWhiteSpace(query)) return string.Empty;
            
            Regex? regex = null;
            if (isRegex)
            {
                try
                {
                    var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    regex = new Regex(query, options, UserRegexMatchTimeout);
                }
                catch (Exception ex)
                {
                    return $"Error: Invalid regular expression. {ex.Message}";
                }
            }

            var results = new List<SearchResult>();

            var index = Volatile.Read(ref _snapshot);
            var docsToSearch = index.Documents.Values.AsEnumerable();
            if (!includeNetCode)
            {
                docsToSearch = docsToSearch.Where(d => !d.IsNetCode);
            }
            if (!string.IsNullOrWhiteSpace(fileFilter))
            {
                docsToSearch = docsToSearch.Where(d => d.RelativePath.Contains(fileFilter, StringComparison.OrdinalIgnoreCase));
            }

            // A model-supplied pattern is bounded per match and across the whole search; literal searches are not.
            var searchStopwatch = Stopwatch.StartNew();
            try
            {
                foreach (var doc in docsToSearch)
                {
                    if (regex != null && searchStopwatch.Elapsed > UserRegexSearchDeadline)
                    {
                        return RegexTooSlowMessage;
                    }

                    var matches = new List<int>();
                    for (int i = 0; i < doc.ContentLines.Length; i++)
                    {
                        if (isRegex && regex != null)
                        {
                            if (regex.IsMatch(doc.ContentLines[i]))
                            {
                                matches.Add(i);
                            }
                        }
                        else
                        {
                            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                            if (doc.ContentLines[i].Contains(query, comparison))
                            {
                                matches.Add(i);
                            }
                        }
                    }

                    if (matches.Any())
                    {
                        results.Add(new SearchResult { Document = doc, MatchLines = matches });
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return RegexTooSlowMessage;
            }
            
            // Sort by number of matches (descending)
            results = results.OrderByDescending(r => r.MatchLines.Count).Take(maxResults).ToList();
            
            if (!results.Any()) return string.Empty;
            
            if (filenamesOnly)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var result in results)
                {
                    sb.AppendLine($"{result.Document.RelativePath} ({result.MatchLines.Count} matches)");
                }
                return sb.ToString().Trim();
            }
            
            var resultSb = new System.Text.StringBuilder();
            
            foreach (var result in results)
            {
                // Group nearby matches within ±5 lines
                var groups = new List<List<int>>();
                foreach (var line in result.MatchLines)
                {
                    if (!groups.Any())
                    {
                        groups.Add(new List<int> { line });
                    }
                    else
                    {
                        var lastGroup = groups.Last();
                        if (line - lastGroup.Last() <= contextLines * 2)
                        {
                            lastGroup.Add(line);
                        }
                        else
                        {
                            groups.Add(new List<int> { line });
                        }
                    }
                }
                
                // Limit to 5 match groups per file
                int totalGroups = groups.Count;
                var groupsToProcess = groups.Take(5).ToList();
                
                foreach (var group in groupsToProcess)
                {
                    int startLine = Math.Max(0, group.First() - contextLines);
                    int endLine = Math.Min(result.Document.ContentLines.Length - 1, group.Last() + contextLines);
                    
                    resultSb.AppendLine($"--- {result.Document.RelativePath}:L{startLine + 1} ---");
                    for (int i = startLine; i <= endLine; i++)
                    {
                        string prefix = group.Contains(i) ? ">>> " : "    ";
                        resultSb.AppendLine($"{prefix}{i + 1}: {result.Document.ContentLines[i]}");
                    }
                    resultSb.AppendLine();
                }
                
                if (totalGroups > 5)
                {
                    resultSb.AppendLine($"[... {totalGroups - 5} additional match groups in this file hidden ...]");
                    resultSb.AppendLine();
                }
            }
            
            string finalResult = resultSb.ToString();
            if (finalResult.Length > maxResultLength)
            {
                finalResult = finalResult.Substring(0, maxResultLength) + "\n\n[... output truncated ...]\n[Additional matches not shown — refine your query or use source_code_view]";
            }
            
            return finalResult.Trim();
        }

        public string FindDefinition(string symbol, string kind)
        {
            var results = new System.Text.StringBuilder();
            var index = Volatile.Read(ref _snapshot);

            // Prioritize .c files over .h files for definitions
            var docsToSearch = index.Documents.Values
                .Where(d => !d.IsNetCode)
                .OrderBy(d => d.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
                
            string escapedSymbol = Regex.Escape(symbol);
            int maxResults = 10;
            int resultCount = 0;

            // Compiled once per call: the per-line loop below only ever calls IsMatch on these.
            var functionRegex = new Regex($@"^{escapedSymbol}\s*\(");
            // Definitions written as "<type tokens> [*]name(" on one line, e.g. "void foo(int x)".
            // Excludes extern/prototype declarations and control-flow keywords like "return foo("
            // or "else foo(" so a call or a prototype is never mistaken for a definition.
            var functionDeclRegex = new Regex($@"^(?!(?:extern|return|else|if|while|for|switch|case|goto|sizeof)\b)(?:[A-Za-z_]\w*\s+|\*\s*)+\**{escapedSymbol}\s*\((?!.*;\s*$)");
            var macroRegex = new Regex($@"^\s*#define\s+{escapedSymbol}[\s(]");
            var structRegex = new Regex($@"^\s*struct\s+{escapedSymbol}\s*{{");
            var typeRegex = new Regex($@"^\s*typedef\s+.*\s+{escapedSymbol}\s*;");
            // Closing line of a multi-line typedef block, e.g. "} gbuf_entry;".
            var typedefCloseRegex = new Regex($@"^\s*\}}\s*{escapedSymbol}\s*;");
            var enumRegex = new Regex($@"^\s*enum\s+{escapedSymbol}\s*{{");
            var enumValueRegex = new Regex($@"^\s*{escapedSymbol}\s*=\s*\d+");
            var enumCommaRegex = new Regex($@"^\s*{escapedSymbol}\s*,");

            foreach (var doc in docsToSearch)
            {
                bool isCFile = doc.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase);

                for (int i = 0; i < doc.ContentLines.Length; i++)
                {
                    string line = doc.ContentLines[i];
                    // Literal gate ahead of the patterns below: each embeds the escaped symbol and none
                    // is case-insensitive, so a line missing the symbol's exact characters cannot match.
                    if (!line.Contains(symbol, StringComparison.Ordinal)) continue;
                    bool match = false;
                    int contextLines = 5;

                    if ((kind == "any" || kind == "function") && isCFile)
                    {
                        // Function definition: match symbol at start of line followed by (
                        if (functionRegex.IsMatch(line) && !line.TrimEnd().EndsWith(";"))
                        {
                            match = true;
                            // For C functions, include the preceding line for the return type
                            contextLines = 8;
                        }
                        else if (functionDeclRegex.IsMatch(line))
                        {
                            match = true;
                            contextLines = 8;
                        }
                    }

                    if ((kind == "any" || kind == "macro") && !match)
                    {
                        if (macroRegex.IsMatch(line))
                        {
                            match = true;
                        }
                    }

                    if ((kind == "any" || kind == "struct") && !match)
                    {
                        if (structRegex.IsMatch(line))
                        {
                            match = true;
                        }
                    }

                    if ((kind == "any" || kind == "type") && !match)
                    {
                        // Also matches the closing line of a multi-line typedef block.
                        if (typeRegex.IsMatch(line) || typedefCloseRegex.IsMatch(line))
                        {
                            match = true;
                        }
                    }

                    if ((kind == "any" || kind == "enum") && !match)
                    {
                        if (enumRegex.IsMatch(line))
                        {
                            match = true;
                        }
                        else if (enumValueRegex.IsMatch(line) || enumCommaRegex.IsMatch(line))
                        {
                            if (doc.ContentLines.Take(i).Reverse().Take(50).Any(l => l.Contains("enum ")))
                            {
                                match = true;
                            }
                        }
                    }

                    if (match)
                    {
                        int startLine = Math.Max(0, i - (kind == "function" || kind == "any" ? 2 : 1));
                        int endLine = Math.Min(doc.ContentLines.Length - 1, i + contextLines);
                        
                        results.AppendLine($"--- {doc.RelativePath}:L{i + 1} ---");
                        for (int j = startLine; j <= endLine; j++)
                        {
                            string prefix = (j == i) ? ">>> " : "    ";
                            results.AppendLine($"{prefix}{j + 1}: {doc.ContentLines[j]}");
                        }
                        results.AppendLine();
                        
                        resultCount++;
                        if (resultCount >= maxResults)
                        {
                            results.AppendLine($"[... Found {maxResults} definitions, stopping search ...]");
                            return results.ToString().Trim();
                        }
                        
                        // Skip ahead so we don't match multiple times in the same context
                        i = endLine;
                    }
                }
            }

            return results.Length > 0 ? results.ToString().Trim() : $"No definition found for '{symbol}' of kind '{kind}'.";
        }
        /// <summary>
        /// Shared helper: finds the first line in the index that matches a symbol of the given kind.
        /// Returns the document and 0-based line index, or (null, -1) if not found.
        /// </summary>
        private static (SourceDocument? doc, int line) FindSymbolLine(SourceIndexSnapshot index, string name, string kind)
        {
            var docsToSearch = index.Documents.Values
                .Where(d => !d.IsNetCode)
                .OrderBy(d => d.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();

            string escapedSymbol = Regex.Escape(name);

            // Compiled once per call, mirroring FindDefinition's patterns.
            var functionRegex = new Regex($@"^{escapedSymbol}\s*\(");
            var functionDeclRegex = new Regex($@"^(?!(?:extern|return|else|if|while|for|switch|case|goto|sizeof)\b)(?:[A-Za-z_]\w*\s+|\*\s*)+\**{escapedSymbol}\s*\((?!.*;\s*$)");
            var macroRegex = new Regex($@"^\s*#define\s+{escapedSymbol}[\s(]");
            var structRegex = new Regex($@"^\s*struct\s+{escapedSymbol}\s*{{");
            var enumRegex = new Regex($@"^\s*enum\s+{escapedSymbol}\s*{{");
            var typeRegex = new Regex($@"^\s*typedef\s+.*\s+{escapedSymbol}\s*;");
            // Closing line of a multi-line typedef block, e.g. "} gbuf_entry;".
            var typedefCloseRegex = new Regex($@"^\s*\}}\s*{escapedSymbol}\s*;");

            foreach (var doc in docsToSearch)
            {
                bool isCFile = doc.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase);

                for (int i = 0; i < doc.ContentLines.Length; i++)
                {
                    string line = doc.ContentLines[i];
                    bool match = false;

                    if ((kind == "any" || kind == "function") && isCFile)
                    {
                        if (functionRegex.IsMatch(line) && !line.TrimEnd().EndsWith(";")) match = true;
                        else if (functionDeclRegex.IsMatch(line)) match = true;
                    }
                    if ((kind == "any" || kind == "macro") && !match)
                    {
                        if (macroRegex.IsMatch(line)) match = true;
                    }
                    if ((kind == "any" || kind == "struct") && !match)
                    {
                        // Opening line of "struct name { ... }", or the closing line of an
                        // anonymous "typedef struct { ... } name;" block (get_function_definition
                        // exposes no "type" kind, so an anonymous typedef surfaces under "struct").
                        if (structRegex.IsMatch(line) || typedefCloseRegex.IsMatch(line)) match = true;
                    }
                    if ((kind == "any" || kind == "enum") && !match)
                    {
                        if (enumRegex.IsMatch(line)) match = true;
                    }
                    if ((kind == "any" || kind == "type") && !match)
                    {
                        // Also matches the closing line of a multi-line typedef block.
                        if (typeRegex.IsMatch(line) || typedefCloseRegex.IsMatch(line)) match = true;
                    }

                    if (match) return (doc, i);
                }
            }
            return (null, -1);
        }

        public string GetFunctionBody(string name, string kind, int? startLineReq = null)
        {
            var (matchDoc, matchLine) = FindSymbolLine(Volatile.Read(ref _snapshot), name, kind);

            if (matchDoc == null || matchLine < 0)
            {
                return $"No definition found for '{name}' of kind '{kind}'.";
            }

            // Decide how to extract
            string resultText = "";
            int extractStart = Math.Max(0, matchLine - (kind == "function" || kind == "any" ? 2 : 1));
            int extractEnd = matchLine;
            bool isMacro = Regex.IsMatch(matchDoc.ContentLines[matchLine], $@"^\s*#define");
            bool isTypedefClose = Regex.IsMatch(matchDoc.ContentLines[matchLine], $@"^\s*\}}\s*{Regex.Escape(name)}\s*;");

            if (isTypedefClose)
            {
                // Multi-line typedef block: walk back to its "typedef struct/union/enum" opener,
                // bounded so a missing opener cannot scan the whole file.
                int opener = -1;
                int scanStart = Math.Max(0, matchLine - 400);
                for (int i = matchLine - 1; i >= scanStart; i--)
                {
                    if (Regex.IsMatch(matchDoc.ContentLines[i], @"^\s*typedef\s+(struct|union|enum)\b"))
                    {
                        opener = i;
                        break;
                    }
                }

                if (opener >= 0)
                {
                    extractStart = opener;
                    extractEnd = matchLine;
                    var sb = new System.Text.StringBuilder();
                    for (int i = extractStart; i <= extractEnd; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                    resultText = sb.ToString();
                }
                else
                {
                    // No opener within the walk-back bound: fall back to the closing line's own context.
                    extractEnd = Math.Min(matchDoc.ContentLines.Length - 1, matchLine + 10);
                    var sb = new System.Text.StringBuilder();
                    for (int i = extractStart; i <= extractEnd; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                    resultText = sb.ToString();
                }
            }
            else if (isMacro)
            {
                // Macro: read lines until no continuation
                int current = matchLine;
                while (current < matchDoc.ContentLines.Length && matchDoc.ContentLines[current].EndsWith("\\"))
                {
                    current++;
                }
                extractEnd = current;
                var sb = new System.Text.StringBuilder();
                for (int i = extractStart; i <= extractEnd; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                resultText = sb.ToString();
            }
            else
            {
                // Function/Struct: look for { and use lexer
                var extraction = CLexer.ExtractBracedBlock(matchDoc.ContentLines, matchLine);
                if (extraction != null)
                {
                    extractEnd = extraction.EndLine;
                    var sb = new System.Text.StringBuilder();
                    for (int i = extractStart; i < extraction.StartLine; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                    sb.AppendLine(extraction.Content);
                    resultText = sb.ToString();
                }
                else
                {
                    // Fallback to simple context
                    extractEnd = Math.Min(matchDoc.ContentLines.Length - 1, matchLine + 10);
                    var sb = new System.Text.StringBuilder();
                    for (int i = extractStart; i <= extractEnd; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                    resultText = sb.ToString();
                }
            }

            string[] resultLines = resultText.Split('\n');
            if (resultLines.Length > 0 && resultLines[resultLines.Length - 1].Length == 0)
            {
                // Every extraction branch above ends with one AppendLine call, so resultText always
                // ends with exactly one newline; Split leaves one empty element beyond the real
                // content. Strip it so output lines map 1:1 onto the extracted file lines.
                resultLines = resultLines.Take(resultLines.Length - 1).ToArray();
            }
            int totalLines = resultLines.Length;

            string headerLine = $"--- {matchDoc.RelativePath}:L{extractStart + 1}-L{extractEnd + 1} ({name}, {totalLines} lines) ---";

            // start_line accepts two numberings: output-relative (1-based, as printed by the
            // truncation notice's start_line value) and absolute file line (1-based, within the
            // header's L-range). A value that fits neither range is never clamped — it is reported
            // as an explicit error with no body, so a stale or mistaken value cannot silently return
            // the wrong lines.
            int startOutputLine;
            if (startLineReq == null)
            {
                startOutputLine = 0;
            }
            else
            {
                int v = startLineReq.Value;
                // 0 is the model's idiom for "from the beginning" and is never printed by a
                // notice, so it cannot be stale.
                if (v == 0) startOutputLine = 0;
                else if (v >= 1 && v <= totalLines)
                {
                    startOutputLine = v - 1;
                }
                else if (v > totalLines && v >= extractStart + 1 && v <= extractEnd + 1)
                {
                    startOutputLine = v - (extractStart + 1);
                }
                else
                {
                    return $"{headerLine}{Environment.NewLine}start_line {v} is outside this definition: output lines 1–{totalLines}, file lines {extractStart + 1}–{extractEnd + 1}. Call again with a value in either range.";
                }
            }

            var finalSb = new System.Text.StringBuilder();
            finalSb.AppendLine(headerLine);

            int maxLines = _maxFunctionBodyLines;
            int outputCount = 0;

            for (int i = startOutputLine; i < totalLines; i++)
            {
                finalSb.AppendLine(resultLines[i].TrimEnd('\r'));
                outputCount++;
                if (outputCount >= maxLines && i < totalLines - 1)
                {
                    // The next unseen line is output line i+2 (1-based) / file line extractStart+i+2 —
                    // both name the same line, so either can be passed back as start_line.
                    finalSb.AppendLine($"\n[Output truncated at line {i + 1} of {totalLines}. Call again with start_line={i + 2} (file line {extractStart + i + 2}) to continue.]");
                    break;
                }
            }

            return finalSb.ToString().Trim();
        }

        /// <summary>Characters <see cref="GetFileExcerpt"/> holds back from a maxChars budget for its truncation notice.</summary>
        private const int NoticeReserve = 160;

        public string GetFileExcerpt(string relativePath, int? startLineReq, int lineCount, string? searchTerm = null, int maxChars = 0)
        {
            // Normalize path for lookup
            relativePath = relativePath.Replace('\\', '/');
            var index = Volatile.Read(ref _snapshot);
            if (!index.Documents.TryGetValue(relativePath, out var doc))
            {
                // Try case-insensitive fuzzy match if exact match fails
                doc = index.Documents.Values.FirstOrDefault(d => d.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
                if (doc == null)
                {
                    return $"Error: File '{relativePath}' not found in indexed source code.";
                }
            }
            
            int startLine = 1;
            
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                int matchLine = -1;
                for (int i = 0; i < doc.ContentLines.Length; i++)
                {
                    if (doc.ContentLines[i].Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        matchLine = i;
                        break;
                    }
                }
                
                if (matchLine == -1)
                {
                    return $"Error: Search term '{searchTerm}' not found in file '{doc.RelativePath}'.";
                }
                
                startLine = Math.Max(1, matchLine + 1 - (lineCount / 2));
            }
            else if (startLineReq.HasValue)
            {
                startLine = startLineReq.Value;
            }
            else
            {
                return "Error: Either start_line or search_term must be provided.";
            }
            
            startLine = Math.Max(1, startLine) - 1; // 0-indexed
            lineCount = Math.Clamp(lineCount, 1, 1000);
            
            if (startLine >= doc.ContentLines.Length)
            {
                return $"Error: File '{doc.RelativePath}' only has {doc.ContentLines.Length} lines. Requested start line {startLine + 1} is out of bounds.";
            }
            
            int endLine = Math.Min(doc.ContentLines.Length - 1, startLine + lineCount - 1);
            int requestedLines = endLine - startLine + 1;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- {doc.RelativePath}:L{startLine + 1}-L{endLine + 1} ---");

            // The header keeps the requested range whatever the budget allows, so the notice's
            // numbers can be checked against it.
            int budget = maxChars > 0 ? maxChars - NoticeReserve : 0;
            int shown = 0;
            int lastShown = startLine;

            for (int i = startLine; i <= endLine; i++)
            {
                string rendered = $"{i + 1}: {doc.ContentLines[i]}";

                // At least one body line is always emitted, however small the budget.
                if (budget > 0 && shown > 0 && sb.Length + rendered.Length + Environment.NewLine.Length > budget)
                {
                    sb.AppendLine($"[Output truncated at line {shown} of {requestedLines} requested (file line {lastShown + 1}). Call again with start_line={lastShown + 2} to continue.]");
                    break;
                }

                sb.AppendLine(rendered);
                shown++;
                lastShown = i;
            }

            return sb.ToString();
        }

        /// <summary>The indexed content lines for a file, resolved the same way as <see cref="GetFileExcerpt"/>, or null when not indexed.</summary>
        public string[]? GetIndexedLines(string relativePath)
        {
            relativePath = relativePath.Replace('\\', '/');
            var index = Volatile.Read(ref _snapshot);
            if (index.Documents.TryGetValue(relativePath, out var doc)) return doc.ContentLines;

            doc = index.Documents.Values.FirstOrDefault(d => d.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
            return doc?.ContentLines;
        }

        public IEnumerable<ConstantInfo> GetConstants(string namePattern, string? prefixFilter)
        {
            var results = Volatile.Read(ref _snapshot).Constants.Values.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(prefixFilter))
            {
                results = results.Where(c => c.Name.StartsWith(prefixFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            if (!string.IsNullOrWhiteSpace(namePattern))
            {
                if (namePattern.Contains("*"))
                {
                    string collapsed = Regex.Replace(namePattern, @"\*+", "*");
                    string pattern = "^" + Regex.Escape(collapsed).Replace("\\*", ".*") + "$";
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase, UserRegexMatchTimeout);
                    results = results.Where(c => IsWildcardMatch(regex, c.Name));
                }
                else
                {
                    results = results.Where(c => string.Equals(c.Name, namePattern, StringComparison.OrdinalIgnoreCase));
                }
            }
            
            return results.OrderBy(c => c.Name).Take(100);
        }

        /// <summary>A match that times out counts as no match for that constant.</summary>
        private static bool IsWildcardMatch(Regex regex, string name)
        {
            try
            {
                return regex.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        public string ListFiles(string? pathFilter, bool includeNetCode)
        {
            var docsToSearch = Volatile.Read(ref _snapshot).Documents.Values.AsEnumerable();
            if (!includeNetCode)
            {
                docsToSearch = docsToSearch.Where(d => !d.IsNetCode);
            }
            if (!string.IsNullOrWhiteSpace(pathFilter))
            {
                docsToSearch = docsToSearch.Where(d => d.RelativePath.Contains(pathFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            var sortedDocs = docsToSearch.OrderBy(d => d.RelativePath).ToList();
            if (!sortedDocs.Any()) return string.Empty;
            
            var sb = new System.Text.StringBuilder();
            foreach (var doc in sortedDocs)
            {
                sb.AppendLine($"{doc.RelativePath} ({doc.ContentLines.Length} lines)");
            }
            sb.AppendLine($"Total: {sortedDocs.Count} files indexed");
            return sb.ToString();
        }

        /* Every macro src/monst.c defines a monster with. All five share argument slots 0-26;
           the variants only append animation, enlargement and replacement slots after them. */
        private const string MonsterMacroAlternation = "GENERAL_MON|ENLARGED_ANIMATED_MON|ENLARGED_MON|ANIMATED_MON|MON";

        public StatsResponse<MonsterStats> GetMonsterStats(string name)
        {
            var response = new StatsResponse<MonsterStats>();
            var index = Volatile.Read(ref _snapshot);
            var doc = index.Documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\monst.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/monst.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null)
            {
                response.Error = "src/monst.c is not in the source code index. Ensure the GnollHack repository is indexed. Use monster_lookup or wiki_search as a fallback.";
                return response;
            }

            int matchLine = -1;
            string escapedName = Regex.Escape(name);
            var nameRegex = new Regex($@"^\s*(?:{MonsterMacroAlternation})\(\s*""{escapedName}""", RegexOptions.IgnoreCase);

            for (int i = 0; i < doc.ContentLines.Length; i++)
            {
                if (nameRegex.IsMatch(doc.ContentLines[i]))
                {
                    matchLine = i;
                    break;
                }
            }

            if (matchLine == -1)
            {
                response.Error = $"No monster named '{name}' found in the game data. Try monster_lookup or wiki_search for partial matches, or check the spelling.";
                return response;
            }

            var extraction = CLexer.ExtractParenBlock(doc.ContentLines, matchLine);
            if (extraction == null)
            {
                response.Error = "Failed to parse monster definition block.";
                return response;
            }

            string rawDef = doc.ContentLines[matchLine].TrimStart() + "\n" + string.Join("\n", doc.ContentLines.Skip(matchLine + 1).Take(extraction.EndLine - matchLine));

            try
            {
                /* ExtractParenBlock includes outer parens; strip them before tokenizing */
                string innerContent = ExtractInnerArgs(extraction.Content);
                var tokens = index.Parser.ParseMonsterMacroArgs(innerContent);
                if (tokens.Count >= 24) /* every monster macro has at least 24 top-level args before soundset fields */
                {
                    var stats = new MonsterStats();

                    /* Positional fields 0-5: name, title, description, femalename, commonname, mlet */
                    stats.Fields["mname"] = tokens[0].Trim('"');
                    if (tokens[1] != "None") stats.Fields["mtitle"] = tokens[1].Trim('"');
                    if (tokens[2] != "None") stats.Fields["mdescription"] = tokens[2].Trim('"');
                    if (tokens[3] != "None") stats.Fields["mfemalename"] = tokens[3].Trim('"');
                    if (tokens[4] != "None") stats.Fields["mcommonname"] = tokens[4].Trim('"');
                    stats.Fields["mlet"] = tokens[5].Trim();

                    /* Position 6: LVL(lvl, mov, ac, mc, mr, aln) */
                    ParseSubMacro(tokens[6], stats.Fields, new[] { "mlevel", "mmove", "ac", "mc", "mr", "maligntyp" });

                    /* Position 7: geno flags — e.g. (G_GENO | G_LGROUP | 2) */
                    ParseGenoFlags(tokens[7], stats.Fields);

                    /* Position 8: A(ATTK(...), ...) — 8 attack slots */
                    ParseAttacks(tokens[8], stats.Fields);

                    /* Position 9: SIZ(wt, nut, snd, siz, heads, lightrange, mat) */
                    ParseSubMacro(tokens[9], stats.Fields, new[] { "cwt", "cnutrit", "msound", "msize", "heads", "lightrange", "body_material_type" });

                    /* Position 10: STATS(str, dex, con, intl, wis, cha) */
                    ParseSubMacro(tokens[10], stats.Fields, new[] { "str", "dex", "con", "intl", "wis", "cha" });

                    /* Positions 11-13: mresists, mresists2, mconveys */
                    stats.Fields["mresists"] = ParseFlagField(tokens[11]);
                    stats.Fields["mresists2"] = ParseFlagField(tokens[12]);
                    stats.Fields["mconveys"] = ParseFlagField(tokens[13]);

                    /* Positions 14-21: mflags1 through mflags8 */
                    for (int f = 0; f < 8; f++)
                    {
                        stats.Fields[$"mflags{f + 1}"] = ParseFlagField(tokens[14 + f]);
                    }

                    /* Position 22: difficulty */
                    stats.Fields["difficulty"] = TryParseInt(tokens[22]);

                    /* Position 23: mcolor */
                    stats.Fields["mcolor"] = tokens[23].Trim();

                    /* Level 1 success: populate stats, populate flag_descriptions, leave macro/struct empty */
                    response.Stats = stats;
                    PopulateFlagDescriptions(response, tokens);

                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Level 1 parsing failed for monster '{Name}', falling back to Level 2.", name);
            }

            /* Level 2 fallback: raw dump + context */
            response.RawDefinition = rawDef;
            response.MacroDefinitions = index.Parser.GetMacroDefinitions("MON", "ANIMATED_MON", "ENLARGED_MON", "ENLARGED_ANIMATED_MON", "GENERAL_MON", "LVL", "A", "ATTK", "SIZ", "STATS");
            response.StructDefinitions = index.Parser.GetStructDefinitions("permonst", "attack");
            response.Message = $"Could not parse structured stats for '{name}'. Raw source and context provided for manual interpretation.";
            PopulateFlagDescriptions(response, rawDef);

            return response;
        }

        public IEnumerable<string> SearchMonsters(string query)
        {
            var doc = Volatile.Read(ref _snapshot).Documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\monst.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/monst.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null) return Enumerable.Empty<string>();

            var results = new List<string>();
            var regex = new Regex($@"^\s*(?:{MonsterMacroAlternation})\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (var line in doc.ContentLines)
            {
                var m = regex.Match(line);
                if (m.Success)
                {
                    string monName = m.Groups[1].Value;
                    if (monName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(monName);
                    }
                }
            }
            return results;
        }

        /// <param name="name">The item's bare <c>oc_name</c>, as src/objects.c writes it.</param>
        /// <param name="objectClass">Selects among the object classes that hold an entry of this name.
        /// Null resolves the first in file order and reports the sharing classes in the result.</param>
        public StatsResponse<ItemStats> GetItemStats(string name, string? objectClass = null)
            => GetItemStats(Volatile.Read(ref _snapshot), name, objectClass);

        private StatsResponse<ItemStats> GetItemStats(SourceIndexSnapshot index, string name, string? objectClass)
        {
            var response = new StatsResponse<ItemStats>();
            var doc = index.Documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\objects.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/objects.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null)
            {
                response.Error = "src/objects.c is not in the source code index. Ensure the GnollHack repository is indexed. Use item_lookup or wiki_search as a fallback.";
                return response;
            }

            int matchLine = -1;
            string escapedName = Regex.Escape(name);
            var nameRegex = new Regex($@"^\s*(?:WEAPON|ARMOR|POTION|SCROLL|SPELL|WAND|RING|AMULET|TOOL|GEM|ROCK|COIN|MISCELLANEOUSITEM|SUIT|HELM|CLOAK|SHIELD|GLOVES|BOOTS|SHIRT|ROBE|BRACERS|WEAPONSHIELD|WEAPONBOOTS|WEAPONGLOVES|DRGN_ARMR|FOOD|REAGENT|CHARGEDRING|SPELLTOOL|CONTAINER|WEPTOOL|BOW|PROJECTILE|GENERAL_[A-Z_]+)\(\s*""{escapedName}""", RegexOptions.IgnoreCase);

            for (int i = 0; i < doc.ContentLines.Length; i++)
            {
                if (nameRegex.IsMatch(doc.ContentLines[i]))
                {
                    matchLine = i;
                    break;
                }
            }

            if (matchLine == -1)
            {
                /* A name carrying a character no item name uses is looked up by the part before
                   it; the note and the miss message still quote the name as given. */
                string cut = CutAtFirstNonNameCharacter(name);
                bool cutApplies = cut.Length > 0 && !string.Equals(cut, name.Trim(), StringComparison.Ordinal);
                var resolver = index.ItemResolver;
                string? fullName = cutApplies
                    ? FindExactItemName(resolver, cut, objectClass)
                        ?? FindUniqueOfSuffixItemName(resolver, cut, objectClass)
                        ?? FindUniqueLeadingItemName(resolver, cut, objectClass)
                    : FindUniqueOfSuffixItemName(resolver, name, objectClass)
                        ?? FindUniqueLeadingItemName(resolver, name, objectClass);
                if (fullName != null)
                {
                    var resolved = GetItemStats(index, fullName, objectClass);
                    if (resolved.Error == null)
                    {
                        string note = $"Resolved '{name.Trim()}' to '{fullName}': no item is named '{name.Trim()}'.";
                        resolved.Message = string.IsNullOrEmpty(resolved.Message) ? note : note + " " + resolved.Message;
                        return resolved;
                    }
                }

                response.Error = BuildItemStatsMissMessage(resolver, name);
                return response;
            }

            var extraction = CLexer.ExtractParenBlock(doc.ContentLines, matchLine);
            if (extraction == null)
            {
                response.Error = "Failed to parse item definition block.";
                return response;
            }

            string rawDef = doc.ContentLines[matchLine].TrimStart() + "\n" + string.Join("\n", doc.ContentLines.Skip(matchLine + 1).Take(extraction.EndLine - matchLine));

            /* Level 1 — the named object class values, expanded from the macro chain by
               ObjectsMacroResolver. The lookup key is the bare oc_name, which is the same name
               the regex above matched, so the tool's contract does not change. */
            ItemResolution resolution;
            try
            {
                resolution = index.ItemResolver.Resolve(name, objectClass);
            }
            catch (Exception rex)
            {
                /* Never lose the raw definition to a resolver defect: the raw dump below is a
                   complete answer on its own, and this tool is model-facing. */
                _logger.LogWarning(rex, "Level 1 parsing threw for item '{Name}', falling back to Level 2.", name);
                resolution = ItemResolution.Failed($"The macro resolver threw: {rex.Message}");
            }

            if (resolution.Success)
            {
                response.Stats = new ItemStats { Fields = resolution.Fields };
                response.RawDefinition = rawDef;
                response.Message = resolution.Message;
                PopulateFlagDescriptions(response, resolution.FlagTokens);
                return response;
            }

            _logger.LogInformation(
                "Level 1 parsing did not resolve item '{Name}' ({Reason}); returning the raw definition.",
                name, resolution.FailureReason);

            /* Level 2 — raw dump plus the macro and struct context needed to read it. The name
               regex above matches every armour wrapper, so the macro list has to as well or the
               model is told to interpret definitions it was not given. */
            response.RawDefinition = rawDef;
            response.MacroDefinitions = index.Parser.GetMacroDefinitions("OBJECT", "OBJ", "BITS", "WEAPON", "ARMOR", "SUIT", "HELM", "CLOAK", "SHIELD", "GLOVES", "BOOTS", "SHIRT", "ROBE", "BRACERS", "DRGN_ARMR", "WEAPONSHIELD", "WEAPONBOOTS", "WEAPONGLOVES", "POTION", "SCROLL", "SPELL", "WAND", "RING", "CHARGEDRING", "AMULET", "TOOL", "SPELLTOOL", "WEPTOOL", "CONTAINER", "GEM", "ROCK", "COIN", "MISCELLANEOUSITEM", "FOOD", "REAGENT", "BOW", "PROJECTILE");
            response.StructDefinitions = index.Parser.GetStructDefinitions("objclass");
            response.Message = response.MacroDefinitions.Count > 0
                ? $"Raw source for '{name}', with the macro and struct definitions needed to read it. Interpret the macro invocation against those definitions. Structured values were not available: {resolution.FailureReason}"
                : $"Raw source for '{name}'. No macro definitions were available for it, so interpret the invocation against src/objects.c itself. Structured values were not available: {resolution.FailureReason}";
            PopulateFlagDescriptions(response, rawDef);

            return response;
        }

        private const int ItemStatsMissMaxChars = 600;
        private const int MaxItemStatsSuggestions = 5;

        /// <summary>
        /// <paramref name="name"/>, trimmed, up to its first character outside ASCII letters,
        /// digits, space, <c>'</c> and <c>-</c>, trimmed again: the characters every item name in
        /// src/objects.c is written in. The test is explicit ASCII because
        /// <see cref="char.IsLetter(char)"/> accepts the non-Latin glyphs this cuts.
        /// </summary>
        private static string CutAtFirstNonNameCharacter(string? name)
        {
            string trimmed = name?.Trim() ?? string.Empty;
            int end = 0;
            while (end < trimmed.Length)
            {
                char c = trimmed[end];
                bool isNameCharacter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                    || c == ' ' || c == '\'' || c == '-';
                if (!isNameCharacter)
                {
                    break;
                }
                end++;
            }
            return trimmed.Substring(0, end).Trim();
        }

        /// <summary>
        /// The item name equal (case-insensitive) to <paramref name="name"/>, restricted to
        /// <paramref name="objectClass"/> when given. Null when none.
        /// </summary>
        private static string? FindExactItemName(ObjectsMacroResolver resolver, string name, string? objectClass)
        {
            string trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                return null;
            }

            return resolver.ItemNames
                .Where(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase))
                .Where(n => string.IsNullOrWhiteSpace(objectClass)
                    || resolver.ObjectClassesOf(n).Contains(objectClass, StringComparer.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        /// <summary>
        /// The one item name ending in <c>" of " + name</c> (case-insensitive), restricted to
        /// <paramref name="objectClass"/> when given, so that <c>experience</c> finds
        /// <c>ioun stone of experience</c>. Null when no name or more than one name matches.
        /// </summary>
        private static string? FindUniqueOfSuffixItemName(ObjectsMacroResolver resolver, string name, string? objectClass)
        {
            string trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                return null;
            }

            string suffix = " of " + trimmed;
            var matches = resolver.ItemNames
                .Where(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .Where(n => string.IsNullOrWhiteSpace(objectClass)
                    || resolver.ObjectClassesOf(n).Contains(objectClass, StringComparer.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        /// <summary>
        /// The one item name equal (case-insensitive) to <paramref name="name"/> without its last
        /// word, or else without its last two, and so on, longest prefix first, restricted to
        /// <paramref name="objectClass"/> when given, so that <c>belt of hill giant strength worth
        /// wearing now</c> finds <c>belt of hill giant strength</c>. The remaining prefix must keep
        /// at least two words, so <c>orange potion</c> never resolves to <c>orange</c>. Null when no
        /// prefix matches, or when the first prefix that matches names more than one item.
        /// </summary>
        private static string? FindUniqueLeadingItemName(ObjectsMacroResolver resolver, string name, string? objectClass)
        {
            string[] words = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            for (int drop = 1; words.Length - drop >= 2; drop++)
            {
                string prefix = string.Join(" ", words, 0, words.Length - drop);
                var matches = resolver.ItemNames
                    .Where(n => string.Equals(n, prefix, StringComparison.OrdinalIgnoreCase))
                    .Where(n => string.IsNullOrWhiteSpace(objectClass)
                        || resolver.ObjectClassesOf(n).Contains(objectClass, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();

                if (matches.Count > 0)
                {
                    return matches.Count == 1 ? matches[0] : null;
                }
            }

            return null;
        }

        /// <summary>
        /// The <see cref="StatsResponse{T}.Error"/> get_item_stats returns when no source line names
        /// <paramref name="name"/>: up to <see cref="MaxItemStatsSuggestions"/> distinct near-miss
        /// names from src/objects.c, found via <see cref="ObjectsMacroResolver.FindItemNames"/> —
        /// first against the whole name, then against each word of four or more characters — the
        /// existing pointer to item_lookup and wiki_search, and, whether or not any suggestion was
        /// found, the reminder that an unidentified appearance (<c>hooded cloak</c>, <c>orange
        /// potion</c>, <c>red mushroom</c>) is randomised per game and so has no objects.c entry of
        /// its own. Capped at <see cref="ItemStatsMissMaxChars"/>.
        /// </summary>
        private static string BuildItemStatsMissMessage(ObjectsMacroResolver resolver, string name)
        {
            var suggestions = new List<string>();

            void AddCandidates(string query)
            {
                if (suggestions.Count >= MaxItemStatsSuggestions || string.IsNullOrWhiteSpace(query))
                {
                    return;
                }

                foreach (var candidate in resolver.FindItemNames(query))
                {
                    if (suggestions.Count >= MaxItemStatsSuggestions) break;
                    if (!suggestions.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    {
                        suggestions.Add(candidate);
                    }
                }
            }

            AddCandidates(name);
            foreach (Match wordMatch in Regex.Matches(name, @"[A-Za-z0-9]{4,}"))
            {
                AddCandidates(wordMatch.Value);
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"No item named '{name}' found in the game data.");
            if (suggestions.Count > 0)
            {
                sb.Append($" Did you mean: {string.Join("; ", suggestions.Take(MaxItemStatsSuggestions))}.");
            }
            sb.Append(" Try item_lookup or wiki_search for partial matches, or check the spelling.");
            sb.Append(" A name that is an unidentified appearance — 'hooded cloak', 'orange potion', 'red mushroom' — has no entry: appearances are randomized per game; see the snapshot's Discoveries section.");

            string message = sb.ToString();
            return message.Length > ItemStatsMissMaxChars ? message.Substring(0, ItemStatsMissMaxChars) : message;
        }

        public IEnumerable<string> SearchItems(string query)
        {
            var doc = Volatile.Read(ref _snapshot).Documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\objects.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/objects.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null) return Enumerable.Empty<string>();

            var results = new List<string>();
            var regex = new Regex(@"^\s*(?:WEAPON|ARMOR|POTION|SCROLL|SPELL|WAND|RING|AMULET|TOOL|GEM|ROCK|COIN|MISCELLANEOUSITEM|SUIT|HELM|CLOAK|SHIELD|GLOVES|BOOTS|SHIRT|ROBE|BRACERS|WEAPONSHIELD|WEAPONBOOTS|WEAPONGLOVES|DRGN_ARMR|FOOD|REAGENT|CHARGEDRING|SPELLTOOL|CONTAINER|WEPTOOL|BOW|PROJECTILE|GENERAL_[A-Z_]+)\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (var line in doc.ContentLines)
            {
                var m = regex.Match(line);
                if (m.Success)
                {
                    string itemName = m.Groups[1].Value;
                    if (itemName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(itemName);
                    }
                }
            }
            return results;
        }

        public StatsResponse<ArtifactStats> GetArtifactStats(string name)
        {
            var response = new StatsResponse<ArtifactStats>();
            var index = Volatile.Read(ref _snapshot);
            var doc = index.Documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\artilist.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/artilist.h", StringComparison.OrdinalIgnoreCase));
            if (doc == null)
            {
                response.Error = "include/artilist.h is not in the source code index. Ensure the GnollHack repository is indexed. Use wiki_search as a fallback.";
                return response;
            }

            int matchLine = -1;
            bool isGeneralArtifact = false;
            string escapedName = Regex.Escape(name);
            var nameRegex = new Regex($@"^\s*(?:GENERAL_ARTIFACT|A)\(\s*""{escapedName}""", RegexOptions.IgnoreCase);

            for (int i = 0; i < doc.ContentLines.Length; i++)
            {
                if (nameRegex.IsMatch(doc.ContentLines[i]))
                {
                    matchLine = i;
                    isGeneralArtifact = doc.ContentLines[i].TrimStart().StartsWith("GENERAL_ARTIFACT(", StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }

            if (matchLine == -1)
            {
                response.Error = $"No artifact named '{name}' found in the game data. Try wiki_search for partial matches, or check the spelling.";
                return response;
            }

            var extraction = CLexer.ExtractParenBlock(doc.ContentLines, matchLine);
            if (extraction == null)
            {
                response.Error = "Failed to parse artifact definition block.";
                return response;
            }

            string rawDef = doc.ContentLines[matchLine].TrimStart() + "\n" + string.Join("\n", doc.ContentLines.Skip(matchLine + 1).Take(extraction.EndLine - matchLine));

            try
            {
                /* ExtractParenBlock includes outer parens; strip them before tokenizing */
                string innerContent = ExtractInnerArgs(extraction.Content);
                var tokens = index.Parser.ParseMonsterMacroArgs(innerContent);
                int minTokens = isGeneralArtifact ? 37 : 34;
                if (tokens.Count >= minTokens)
                {
                    var stats = new ArtifactStats();

                    /* Positional fields for A() macro — 34 parameters */
                    /* 0: name */
                    stats.Fields["name"] = tokens[0].Trim('"');
                    /* 1: desc (unidentified name) */
                    if (tokens[1].Trim() != "None") stats.Fields["desc"] = tokens[1].Trim('"');
                    /* 2: hit_desc */
                    if (tokens[2].Trim() != "None") stats.Fields["hit_desc"] = tokens[2].Trim('"');
                    /* 3: typ (base object type) */
                    stats.Fields["otyp"] = tokens[3].Trim();
                    /* 4: masktyp */
                    stats.Fields["maskotyp"] = tokens[4].Trim();
                    /* 5: material */
                    stats.Fields["material"] = tokens[5].Trim();
                    /* 6: exceptionality */
                    stats.Fields["exceptionality"] = tokens[6].Trim();
                    /* 7: mythic_prefix */
                    stats.Fields["mythic_prefix"] = tokens[7].Trim();
                    /* 8: mythic_suffix */
                    stats.Fields["mythic_suffix"] = tokens[8].Trim();
                    /* 9: aflags */
                    stats.Fields["aflags"] = ParseFlagField(tokens[9]);
                    /* 10: aflags2 */
                    stats.Fields["aflags2"] = ParseFlagField(tokens[10]);
                    /* 11: spfx (wielded/worn special effects) */
                    stats.Fields["spfx"] = ParseFlagField(tokens[11]);
                    /* 12: cspfx (carried special effects) */
                    stats.Fields["cspfx"] = ParseFlagField(tokens[12]);
                    /* 13: mtype (monster type, symbol, or flag) */
                    stats.Fields["mtype"] = tokens[13].Trim();
                    /* 14-16: tohit dice */
                    stats.Fields["tohit_dice"] = TryParseInt(tokens[14]);
                    stats.Fields["tohit_diesize"] = TryParseInt(tokens[15]);
                    stats.Fields["tohit_plus"] = TryParseInt(tokens[16]);
                    /* 17: attk (attack sub-macro, e.g. PHYS(1,10)) */
                    stats.Fields["attk"] = tokens[17].Trim();
                    /* 18: worn_prop (defense property when wielded/worn) */
                    stats.Fields["worn_prop"] = tokens[18].Trim();
                    /* 19: carried_prop */
                    stats.Fields["carried_prop"] = tokens[19].Trim();
                    /* 20: inv_prop (invoke property) */
                    stats.Fields["inv_prop"] = tokens[20].Trim();
                    /* 21-23: invoke duration dice */
                    stats.Fields["inv_duration_dice"] = TryParseInt(tokens[21]);
                    stats.Fields["inv_duration_diesize"] = TryParseInt(tokens[22]);
                    stats.Fields["inv_duration_plus"] = TryParseInt(tokens[23]);
                    /* 24: inv_mana_cost */
                    stats.Fields["inv_mana_cost"] = TryParseInt(tokens[24]);
                    /* 25: repower_time */
                    stats.Fields["repower_time"] = TryParseInt(tokens[25]);
                    /* 26: alignment */
                    stats.Fields["alignment"] = tokens[26].Trim();
                    /* 27: role */
                    stats.Fields["role"] = tokens[27].Trim();
                    /* 28: race */
                    stats.Fields["race"] = tokens[28].Trim();
                    /* 29: cost */
                    string costVal = tokens[29].Trim().TrimEnd('L', 'l');
                    stats.Fields["cost"] = TryParseInt(costVal);
                    /* 30: acolor (glow color) */
                    stats.Fields["acolor"] = tokens[30].Trim();
                    /* 31: ocolor (object color override) */
                    stats.Fields["ocolor"] = tokens[31].Trim();
                    /* 32: tile_floor_height */
                    stats.Fields["tile_floor_height"] = TryParseInt(tokens[32]);
                    /* 33: soundset */
                    stats.Fields["soundset"] = tokens[33].Trim();

                    /* GENERAL_ARTIFACT has 3 extra fields */
                    if (isGeneralArtifact && tokens.Count >= 37)
                    {
                        stats.Fields["stand_animation"] = tokens[34].Trim();
                        stats.Fields["enlargement"] = tokens[35].Trim();
                        stats.Fields["replacement"] = tokens[36].Trim();
                    }

                    /* Level 1 success */
                    response.Stats = stats;
                    PopulateFlagDescriptions(response, tokens);

                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Level 1 parsing failed for artifact '{Name}', falling back to Level 2.", name);
            }

            /* Level 2 fallback: raw dump + context */
            response.RawDefinition = rawDef;
            response.MacroDefinitions = index.Parser.GetMacroDefinitions("GENERAL_ARTIFACT", "A", "PHYS", "PHYSI", "DRLI", "COLD", "FIRE", "ELEC", "STUN", "NO_ATTK");
            response.StructDefinitions = index.Parser.GetStructDefinitions("artifact", "attack");
            response.Message = $"Could not parse structured stats for '{name}'. Raw source and context provided for manual interpretation.";
            PopulateFlagDescriptions(response, rawDef);

            return response;
        }

        public IEnumerable<string> SearchArtifacts(string query)
        {
            var doc = Volatile.Read(ref _snapshot).Documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\artilist.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/artilist.h", StringComparison.OrdinalIgnoreCase));
            if (doc == null) return Enumerable.Empty<string>();

            var results = new List<string>();
            var regex = new Regex(@"^\s*(?:GENERAL_ARTIFACT|A)\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (var line in doc.ContentLines)
            {
                var m = regex.Match(line);
                if (m.Success)
                {
                    string artifactName = m.Groups[1].Value;
                    if (!string.IsNullOrEmpty(artifactName) && artifactName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(artifactName);
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// The roles, races and their abilities parsed in the published pass; null before a pass has
        /// parsed them, and always for a service whose <see cref="ParseGameData"/> parses nothing.
        /// </summary>
        public CharacterTables? GetCharacterTables()
            => _characterTables.TryGetValue(Volatile.Read(ref _snapshot).Parser, out var tables) ? tables : null;

        /* --- Helper methods for monster stats Level 1 parsing --- */

        /// <summary>
        /// Parse a sub-macro like LVL(3, 9, 8, 0, 0, -4) into named fields.
        /// </summary>
        private static void ParseSubMacro(string token, Dictionary<string, object> fields, string[] fieldNames)
        {
            string inner = ExtractInnerArgs(token);
            var args = SplitTopLevelCommas(inner);
            for (int i = 0; i < fieldNames.Length && i < args.Count; i++)
            {
                string val = args[i].Trim();
                fields[fieldNames[i]] = TryParseInt(val);
            }
        }

        /// <summary>
        /// Parse geno flags like (G_GENO | G_LGROUP | 2) into a list of flag names + frequency.
        /// </summary>
        private static void ParseGenoFlags(string token, Dictionary<string, object> fields)
        {
            string inner = token.Trim();
            /* Remove outer parens if present */
            if (inner.StartsWith("(") && inner.EndsWith(")"))
                inner = inner.Substring(1, inner.Length - 2).Trim();

            var parts = inner.Split('|').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            var flagNames = new List<string>();
            int? frequency = null;
            foreach (var part in parts)
            {
                if (int.TryParse(part, out int freq))
                    frequency = freq;
                else
                    flagNames.Add(part);
            }
            fields["geno"] = flagNames;
            if (frequency.HasValue)
                fields["geno_frequency"] = frequency.Value;
        }

        /// <summary>
        /// Parse A(ATTK(...), ATTK(...), ..., NO_ATTK, ...) into mattk array.
        /// </summary>
        private static void ParseAttacks(string token, Dictionary<string, object> fields)
        {
            string inner = ExtractInnerArgs(token);
            var attackTokens = SplitTopLevelCommas(inner);
            var attacks = new List<Dictionary<string, object>>();
            var attackFields = new[] { "aatyp", "adtyp", "damn", "damd", "damp", "mcadj", "mlevel", "range", "aflags", "action_tile" };

            foreach (var atkToken in attackTokens)
            {
                string trimmed = atkToken.Trim();
                if (trimmed == "NO_ATTK") continue;
                if (trimmed.StartsWith("ATTK(", StringComparison.OrdinalIgnoreCase))
                {
                    string atkInner = ExtractInnerArgs(trimmed);
                    var atkArgs = SplitTopLevelCommas(atkInner);
                    var attack = new Dictionary<string, object>();
                    for (int i = 0; i < attackFields.Length && i < atkArgs.Count; i++)
                    {
                        string val = atkArgs[i].Trim();
                        /* aflags often have UL suffix */
                        if (val.EndsWith("UL", StringComparison.OrdinalIgnoreCase))
                            val = val.Substring(0, val.Length - 2);
                        attack[attackFields[i]] = TryParseInt(val);
                    }
                    if (attack.TryGetValue("damn", out var damnVal) && damnVal is int damn &&
                        attack.TryGetValue("damd", out var damdVal) && damdVal is int damd &&
                        damn > 0)
                    {
                        attack["dice"] = $"{damn}d{damd}";
                    }
                    attacks.Add(attack);
                }
            }
            fields["mattk"] = attacks;
        }

        /// <summary>
        /// Parse a flag field like "M1_HUMANOID | M1_CARNIVORE" into a list, or a simple value. An
        /// enclosing pair of parentheses is unwrapped first, repeating for a nested full wrapper; an
        /// expression that still contains a parenthesis afterwards (an unbalanced fragment, or a
        /// union of unions like "(A | B) | (C | D)") is returned verbatim rather than split into
        /// fabricated names.
        /// </summary>
        internal static object ParseFlagField(string token)
        {
            string trimmed = token.Trim();
            while (trimmed.Length > 0 && trimmed[0] == '(')
            {
                int matchingClose = FindMatchingParen(trimmed, 0);
                if (matchingClose != trimmed.Length - 1) break;
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }
            if (trimmed.IndexOfAny(new[] { '(', ')' }) >= 0)
            {
                return trimmed;
            }
            if (trimmed.Contains('|'))
            {
                var flags = trimmed.Split('|').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                return flags;
            }
            return trimmed;
        }

        /// <summary>Index of the ')' balancing the '(' at openIndex within text, or -1 if unbalanced.</summary>
        private static int FindMatchingParen(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Extract the inner arguments from a macro call: "LVL(3, 9, 8)" -> "3, 9, 8"
        /// </summary>
        private static string ExtractInnerArgs(string token)
        {
            int openParen = token.IndexOf('(');
            if (openParen < 0) return token;
            int closeParen = token.LastIndexOf(')');
            if (closeParen <= openParen) return token.Substring(openParen + 1);
            return token.Substring(openParen + 1, closeParen - openParen - 1);
        }

        /// <summary>
        /// Split by commas at depth 0 (respecting nested parens).
        /// </summary>
        private static List<string> SplitTopLevelCommas(string text)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(' || c == '{') depth++;
                else if (c == ')' || c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < text.Length) result.Add(text.Substring(start));
            return result;
        }

        /// <summary>
        /// Try to parse a string as integer; return the int if successful, otherwise the trimmed string.
        /// </summary>
        private static object TryParseInt(string val)
        {
            string trimmed = val.Trim();
            if (int.TryParse(trimmed, out int intVal)) return intVal;
            return trimmed;
        }

        /// <summary>
        /// Populate flag_descriptions from tokens list. Uses flag name as own description if not in dictionary (Fix #6).
        /// </summary>
        private void PopulateFlagDescriptions<T>(StatsResponse<T> response, List<string> tokens)
        {
            foreach (var token in tokens)
            {
                foreach (var part in token.Split('|', '(', ')', ' ', '\t', ','))
                {
                    string p = part.Trim();
                    if (string.IsNullOrEmpty(p)) continue;
                    /* Only include tokens that look like flag constants (contain underscore and uppercase) */
                    if (!p.Contains('_') || p.All(c => char.IsDigit(c) || c == '-')) continue;
                    if (_flagDescriptions.TryGetValue(p, out var desc))
                        response.FlagDescriptions[p] = desc;
                    else if (Regex.IsMatch(p, @"^[A-Z][A-Z0-9_]+$"))
                        response.FlagDescriptions[p] = p; /* Flag name as own description */
                }
            }
        }

        /// <summary>
        /// Populate flag_descriptions from raw definition string.
        /// </summary>
        private void PopulateFlagDescriptions<T>(StatsResponse<T> response, string rawDefinition)
        {
            foreach (var part in rawDefinition.Split('|', '(', ')', ',', ' ', '\t', '\n', '\r'))
            {
                string p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;
                if (!p.Contains('_') || p.All(c => char.IsDigit(c) || c == '-')) continue;
                if (_flagDescriptions.TryGetValue(p, out var desc))
                    response.FlagDescriptions[p] = desc;
                else if (Regex.IsMatch(p, @"^[A-Z][A-Z0-9_]+$"))
                    response.FlagDescriptions[p] = p;
            }
        }

        internal sealed class SourceDocument
        {
            public string FilePath { get; init; } = string.Empty;
            public string RelativePath { get; init; } = string.Empty;
            public bool IsNetCode { get; init; }
            public string[] ContentLines { get; init; } = Array.Empty<string>();
        }

        private class SearchResult
        {
            public SourceDocument Document { get; set; } = new SourceDocument();
            public List<int> MatchLines { get; set; } = new List<int>();
        }
    }
}
