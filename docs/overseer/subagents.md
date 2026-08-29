# Overseer Subagent (Multi-Agent) Architecture

This document describes the design and implementation of autonomous subagents in the Overseer project.

## Overview

Overseer supports a **coordinator-specialist multi-agent architecture**:
1. The **Main Coordinator Agent** interacts with the user, analyzes inquiries, and delegates scoped, multi-step sub-tasks to specialist subagents using the `delegate_to_subagent` tool.
2. **Specialist Subagents** (e.g., `wiki_researcher`, `source_investigator`, `game_data_analyst`) run autonomously in their own loop with targeted system instructions, an allowed subset of tools, a dedicated execution budget, and tailored model/credential resolution.
3. Subagents run concurrently under `ToolBatchRunner` when multiple delegation tool calls are issued in a single iteration.
4. Users can inspect subagent actions in real time and individually terminate/cancel any running subagent without stopping the entire conversation.

## Core Components

### 1. Catalog & Definition (`SubAgentCatalogService` & `SubAgentCatalog.json`)
- Registered subagents are defined in `Overseer/Data/SubAgentCatalog.json`.
- Each definition specifies `name`, `displayName`, `description`, `instructions`, `allowedTools`, `maxIterations`, `modelPreference` (`provider` and `modelId`), and reasoning defaults.
- Validated on application startup against the `ToolRegistry` and `ModelMetadataService`.

### 2. Delegation Tool (`DelegateToSubAgentTool`)
- Implemented as an `IToolHandler` under `ToolCategory.SubAgent`.
- Supports parameters `agent_name` (required), `task` (required), `context` (optional), and `subagent_name` (optional, 2–6 word human-readable instance title).
- Automatically personalizes the subagent's seed system instructions with the instance title (`For this delegation you are acting as: "{subagentName}"...`) on a local string without mutating the singleton catalog.
- Validates delegation depth (depth limit 1 prevents infinite recursion).
- Registers the subagent execution with `OngoingChatManager` for individual cancellation.
- Resolves subagent execution model, credentials, and parameters following the precedence hierarchy:
  1. **Subagent Definition Preference** (`modelPreference`): Authorized chat system model matching preference (`ModelRole` of 1 or 3) → `appsettings.json` provider key → user provider API key.
  2. **Active System Model** (`ActiveSystemModelId`): If active in coordinator turn, inherits the authorized system model and credentials (resolved `ModelRole` of 1 (Chat) or 3 (Both); title-only models with `ModelRole = 2` are excluded).
  3. **Active User Model** (`ActiveUserModelId`): If active in coordinator turn, inherits the selected user model and credentials.
  4. **User Default Model**: The user's first configured model in `UserAiModels`.
  5. **First Authorized System Model**: The first assigned system model available for chat (resolved `ModelRole` of 1 or 3).
  6. **AppSettings Fallback**: Default model from `appsettings.json` (`AI:Provider`, `AI:Model`, `AI:APIKey`).
- Output token limits resolve with precedence: Subagent definition override (`SubAgentDefinition.MaxOutputTokens`) → Inherited model row (`MaxOutputTokens`) → Static catalog defaults via `ModelMetadataService` (e.g. 128k for `gpt-5.6-luna`, 65k for `gemini-2.5-pro`).
- Clones and propagates `ToolExecutionContext` (`SessionId`, `UserId`, `SpoilerFreeMode`, `AgentDepth = parent + 1`, `Budget`, `ActiveSystemModelId`, `ActiveUserModelId`, `ParallelExecutionMode`) to maintain isolation and spoiler safety.
- Forwards an explicit allow-list of subagent events (`debug` (when `ShowDebugLog` is true), `tool_start`, `tool_result`, `tool_error`) to the parent event sink while isolating subagent prose and reasoning (`chunk`, `thinking_chunk`), status updates (`status`), timing metrics (`ttft`, `duration`), and top-level errors (`error`) from coordinator chat streams.
- Executes the subagent loop via `AgentLoopRunner`.

### 3. Agent Loop Runner (`AgentLoopRunner`)
- Core multi-turn agent loop driving LLM requests, parallel tool execution with `ToolBatchRunner`, thought markup generation, and error recovery.
- Computes human-friendly display names for subagents via `SubAgentUiHelper` and enriches `tool_start` payloads with `display_name`.
- Centralized clamping of `effectiveMaxOutputTokens` against `runMetadata.MaxOutputTokens` to prevent provider HTTP 400 errors.
- Automatic detection of output truncation (`finishReason: MAX_TOKENS`, `stop_reason: max_tokens`, or `response.incomplete` status) with user-facing truncation notices (`_[Response truncated: output token limit reached.]_`).
- Reusable across the main coordinator agent and specialist subagents.

### 4. Capability Gating & Availability (`SubAgentAvailability`)
- Weak models (e.g., nano or flash-lite models) that lack complex coordination capabilities have `"supportsSubAgentCoordination": false` in their catalogs (`gemini-3.1-flash-lite`, `gemini-3.5-flash-lite`, `gpt-5.4-nano`).
- Models not suited for specialist task execution have `"supportsSubAgentExecution": false` (`gemini-3.1-flash-lite`, `gemini-3.5-flash-lite`, `gpt-5.4-nano`).
- The `delegate_to_subagent` tool is automatically omitted from the available tool list when a non-coordinating model is active.

### 5. Individual Cancellation & Observability
- Each running subagent is registered in `OngoingChatManager.ActiveSubAgents`.
- The user can cancel a specific subagent via `POST /api/chat/sessions/{sessionId}/subagents/{toolCallId}/cancel`.
- Streamed diagnostic events are tagged with `[SubAgent:{agentName}]` and displayed live in the frontend debug log.
- Subagent tool lifecycle events stream live to the client with `display_name`, `agent_name`, `parent_tool_call_id`, and `depth`, rendering as nested tool blocks during active streaming. These events are also buffered into `OngoingChatManager.AccumulatedEvents` so a reconnecting client replays the nested streaming blocks.

### 6. Persistence & Retention
- Subagent tool executions are persisted in `ChatMessageToolCall` rows with `DisplayName`, `AgentName`, `ParentToolCallId`, and `Depth`.
- `DisplayName` is clamped to 256 characters and persisted to the database, ensuring that even when `ChatRetentionService.PruneAgedToolCallResultsAsync` prunes aged `ArgsText` and `Result` fields, human-readable subagent titles remain intact on historical sessions.
- On session load, nested tool calls are reconstructed and displayed in a hierarchical view.

## Configuration Reference (`SubAgentSettings`)

The subagent runtime is configured under the `SubAgentSettings` section in `appsettings.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Master switch enabling or disabling subagent delegation tool availability. |
| `TimeoutSeconds` | `600` | Maximum execution time per subagent run (in seconds). Default is 10 minutes. |
| `MaxParallelSubAgents` | `3` | Maximum number of subagents allowed to run concurrently in a single coordinator turn batch. |
| `MaxProcessParallelSubAgents` | `12` | Maximum process-wide subagent execution slots across all users/sessions in `ToolExecutor`. |
| `SubAgentQueueWaitSeconds` | `30` | Maximum time a subagent call will wait for a process execution slot before fast-failing. |
| `MaxSubAgentRuns` | `6` | Maximum number of subagent invocations per coordinator user turn. |
| `MaxTotalModelCalls` | `48` | Maximum combined LLM API iterations across coordinator and all subagents in a single turn. |
| `MinCallsPerSessionWithSubAgents` | `200` | Minimum per-session tool execution limit floor when subagents are active. |
| `MaxAccumulatedEvents` | `5000` | Maximum bounded event history buffer for SignalR reconnection replays. |


