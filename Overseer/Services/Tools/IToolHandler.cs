using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public interface IToolHandler
    {
        string ToolName { get; }
        string Description { get; set; }
        ToolExecutionLocation ExecutionLocation { get; }
        ToolCategory Category { get; }
        JsonElement ParameterSchema { get; }
        
        bool RequiresConfirmation => false;
        int TimeoutSeconds => 15;

        /// <summary>
        /// Optional per-tool floor for the result length cap, in characters. When set,
        /// the effective cap is max(user setting, this value). Use it only for tools
        /// whose value collapses under truncation - a snapshot cut at an arbitrary
        /// character loses its tail sections (Discoveries, dungeon overview) silently.
        /// Null means the user's MaxResultLength setting governs alone.
        /// </summary>
        int? MaxResultLengthOverride => null;

        Task<ToolResult> ExecuteAsync(JsonElement parameters,
                                      ToolExecutionContext context,
                                      CancellationToken cancellationToken);
    }

    public class ToolResult
    {
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public long? QueueWaitMs { get; set; }
        public long? ExecutionMs { get; set; }
        public List<MobileGnollHackLogger.Data.ChatMessageToolCall>? NestedToolCalls { get; set; }
        public string? TerminationStatus { get; set; }
        public bool BudgetExhausted { get; set; }
        public int? RemainingBudget { get; set; }
    }

    public class ToolExecutionContext
    {
        /// <summary>
        /// The session this turn belongs to -- persisted or ephemeral. A
        /// <see cref="Privacy.SessionRef"/> rather than a <c>long</c> so a handler that queries
        /// <c>ChatSession</c> by it has to say which case it means.
        /// </summary>
        public Privacy.SessionRef SessionId { get; set; }
        public string? ToolBudgetScopeId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public bool SpoilerFreeMode { get; set; }
        public bool IsGameOn { get; set; }
        public int OverseerMode { get; set; }
        public string DataDirectory { get; set; } = string.Empty;
        public bool IsGnollHackSession { get; set; }
        public int MaxResultLength { get; set; } = 10000;
        public int MaxCallsPerSession { get; set; } = 150;
        public string? ToolCallId { get; set; }
        public string? AgentName { get; set; }
        public int AgentDepth { get; set; } = 0;
        public int MaxAgentDepth { get; set; } = 1;
        public int MaxSubAgentResultLength { get; set; } = 30000;
        public Func<ChatEvent, Task>? EventSink { get; set; }
        public Agents.AgentRunBudget? Budget { get; set; }
        public bool ShowDebugLog { get; set; }
        public bool EnableSubAgents { get; set; } = false;
        public long? ActiveUserModelId { get; set; }
        public long? ActiveSystemModelId { get; set; }
        public MobileGnollHackLogger.Data.ParallelExecutionMode ParallelExecutionMode { get; set; } = MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled;

        /// <summary>
        /// Whether this turn runs in a confidential session with tool egress blocked.
        /// </summary>
        /// <remarks>
        /// Carried through <see cref="CloneFor"/> and into every sub-agent context. A sub-agent
        /// inheriting a permissive tool set would reopen the channel the mode exists to close,
        /// which is why the flag travels with the context rather than being consulted once at
        /// the top of the turn.
        /// </remarks>
        public bool BlockExternalEgress { get; set; } = false;

        public ToolExecutionContext CloneFor(string toolCallId)
        {
            return new ToolExecutionContext
            {
                SessionId = this.SessionId,
                ToolBudgetScopeId = this.ToolBudgetScopeId,
                UserId = this.UserId,
                SpoilerFreeMode = this.SpoilerFreeMode,
                IsGameOn = this.IsGameOn,
                OverseerMode = this.OverseerMode,
                DataDirectory = this.DataDirectory,
                IsGnollHackSession = this.IsGnollHackSession,
                MaxResultLength = this.MaxResultLength,
                MaxCallsPerSession = this.MaxCallsPerSession,
                ToolCallId = toolCallId,
                AgentName = this.AgentName,
                AgentDepth = this.AgentDepth,
                MaxAgentDepth = this.MaxAgentDepth,
                MaxSubAgentResultLength = this.MaxSubAgentResultLength,
                EventSink = this.EventSink,
                Budget = this.Budget,
                ShowDebugLog = this.ShowDebugLog,
                EnableSubAgents = this.EnableSubAgents,
                ActiveUserModelId = this.ActiveUserModelId,
                ActiveSystemModelId = this.ActiveSystemModelId,
                ParallelExecutionMode = this.ParallelExecutionMode,
                BlockExternalEgress = this.BlockExternalEgress
            };
        }
    }
}
