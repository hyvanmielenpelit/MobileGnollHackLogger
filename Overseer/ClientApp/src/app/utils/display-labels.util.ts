/**
 * Readable labels for coded values the admin UI shows: tool names, provider names, and the sentence-case fallback
 * for any slug without a curated label. The ids stay the values in code, the API, the URL and stored data.
 */

/**
 * A tool's label as a noun phrase, from the chat's activity phrases (`ChatComponent.TOOL_DISPLAY_NAMES`). The source
 * tools serve both the GnollHack and the NetHack source, so their labels name no corpus. Update it together with
 * `TOOL_DISPLAY_NAMES` when a tool is added.
 */
const TOOL_LABELS: ReadonlyMap<string, string> = new Map([
  ['get_full_message_history', 'Message history'],
  ['get_directory_listing', 'Game folder listing'],
  ['refresh_snapshot', 'Game status refresh'],
  ['get_save_info', 'Save game info'],
  ['get_player_library', 'Manuals'],
  ['get_oracle_consultations', 'Oracle consultations'],
  ['get_player_xlog', 'Recent games'],
  ['get_player_dumplogs', 'Player dumplogs'],
  ['get_app_log', 'Application log'],
  ['get_panic_log', 'Panic log'],
  ['get_performance_reports', 'Performance reports'],
  ['item_lookup', 'GnollHack wiki item lookup'],
  ['monster_lookup', 'GnollHack wiki monster lookup'],
  ['nethack_wiki_search', 'NetHack wiki search'],
  ['nethack_wiki_view', 'NetHack wiki article'],
  ['search_server_dumplogs', 'Server dumplog search'],
  ['source_code_search', 'Source code search'],
  ['source_code_view', 'Source code view'],
  ['wiki_search', 'GnollHack wiki search'],
  ['list_indexed_files', 'Source file list'],
  ['get_constants', 'Constants search'],
  ['get_knowledge_article', 'Knowledge base article'],
  ['wiki_view', 'GnollHack wiki article'],
  ['search_definitions', 'Definitions search'],
  ['get_function_definition', 'Function definition'],
  ['get_item_stats', 'Item stats'],
  ['get_monster_stats', 'Monster stats'],
  ['get_artifact_stats', 'Artifact stats'],
  ['get_github_repo_info', 'GitHub repository info'],
  ['search_github', 'GitHub search'],
  ['delegate_to_subagent', 'Subagent'],
  ['report_feedback', 'Feedback to developers'],
  ['get_character_info', 'Race and role info'],
  ['referenced_by', 'Referenced-by list']
]);

/** Provider names keyed by their lower-case spelling. */
const PROVIDER_LABELS: ReadonlyMap<string, string> = new Map([
  ['openai', 'OpenAI'],
  ['anthropic', 'Anthropic'],
  ['google', 'Google'],
  ['xai', 'xAI']
]);

/**
 * A slug's words in sentence case: `session-create-turn` reads *Session create turn*, `ClientData` *Client data*,
 * `some_new_tool` *Some new tool*. An all-capital word (`HTTP`) keeps its case. Empty for an empty value; a value with
 * no letters or digits is returned trimmed.
 */
export function wordsLabel(value: string | null | undefined): string {
  const text = (value ?? '').trim();
  if (!text) return '';
  const words = text
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .split(/[\s_-]+/)
    .filter(word => word !== '');
  if (words.length === 0) return text;
  return words
    .map((word, index) => {
      if (word.length > 1 && /[A-Z]/.test(word) && word === word.toUpperCase()) return word;
      const lower = word.toLowerCase();
      return index === 0 ? lower.charAt(0).toUpperCase() + lower.slice(1) : lower;
    })
    .join(' ');
}

/** A tool's label (`wiki_search` reads *GnollHack wiki search*); an unknown tool reads in words. */
export function toolLabel(name: string | null | undefined): string {
  const key = (name ?? '').trim();
  return TOOL_LABELS.get(key) ?? wordsLabel(key);
}

/** A provider's name in its own spelling (`openai` reads *OpenAI*); any other value as stored, trimmed. */
export function providerLabel(provider: string | null | undefined): string {
  const text = (provider ?? '').trim();
  return PROVIDER_LABELS.get(text.toLowerCase()) ?? text;
}
