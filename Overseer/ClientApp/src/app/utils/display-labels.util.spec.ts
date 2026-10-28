import { providerLabel, toolLabel, wordsLabel } from './display-labels.util';

describe('display labels', () => {
  describe('wordsLabel', () => {
    it('reads a slug in sentence case', () => {
      expect(wordsLabel('session-create-turn')).toBe('Session create turn');
      expect(wordsLabel('some_new_tool')).toBe('Some new tool');
      expect(wordsLabel('not-assigned')).toBe('Not assigned');
    });

    it('splits camel and Pascal case, and keeps an all-capital word', () => {
      expect(wordsLabel('ClientData')).toBe('Client data');
      expect(wordsLabel('LoginRateLimit')).toBe('Login rate limit');
      expect(wordsLabel('messageHistory')).toBe('Message history');
      expect(wordsLabel('HTTPError')).toBe('HTTP error');
    });

    it('is empty for an empty value and keeps a value without words', () => {
      expect(wordsLabel(null)).toBe('');
      expect(wordsLabel(undefined)).toBe('');
      expect(wordsLabel('   ')).toBe('');
      expect(wordsLabel('--')).toBe('--');
      expect(wordsLabel(' x ')).toBe('X');
    });
  });

  describe('toolLabel', () => {
    it('names the chat tools as noun phrases', () => {
      expect(toolLabel('wiki_search')).toBe('GnollHack wiki search');
      expect(toolLabel('wiki_view')).toBe('GnollHack wiki article');
      expect(toolLabel('nethack_wiki_search')).toBe('NetHack wiki search');
      expect(toolLabel('get_item_stats')).toBe('Item stats');
      expect(toolLabel('get_knowledge_article')).toBe('Knowledge base article');
      expect(toolLabel('report_feedback')).toBe('Feedback to developers');
    });

    it('names the source tools without a corpus, since they serve both', () => {
      for (const name of ['source_code_search', 'source_code_view', 'list_indexed_files', 'get_constants', 'search_definitions', 'get_function_definition']) {
        const label = toolLabel(name);
        expect(label).not.toMatch(/GnollHack|NetHack/);
        expect(label).not.toBe(name);
      }
      expect(toolLabel('source_code_search')).toBe('Source code search');
    });

    it('names the experimental tools', () => {
      expect(toolLabel('get_character_info')).toBe('Race and role info');
      expect(toolLabel('referenced_by')).toBe('Referenced-by list');
    });

    it('labels every tool the chat offers, so no tool name shows through', () => {
      const expected: Record<string, string> = {
        get_full_message_history: 'Message history',
        get_directory_listing: 'Game folder listing',
        refresh_snapshot: 'Game status refresh',
        get_save_info: 'Save game info',
        get_player_library: 'Manuals',
        get_oracle_consultations: 'Oracle consultations',
        get_player_xlog: 'Recent games',
        get_player_dumplogs: 'Player dumplogs',
        get_app_log: 'Application log',
        get_panic_log: 'Panic log',
        get_performance_reports: 'Performance reports',
        item_lookup: 'GnollHack wiki item lookup',
        monster_lookup: 'GnollHack wiki monster lookup',
        nethack_wiki_search: 'NetHack wiki search',
        nethack_wiki_view: 'NetHack wiki article',
        search_server_dumplogs: 'Server dumplog search',
        source_code_search: 'Source code search',
        source_code_view: 'Source code view',
        wiki_search: 'GnollHack wiki search',
        list_indexed_files: 'Source file list',
        get_constants: 'Constants search',
        get_knowledge_article: 'Knowledge base article',
        wiki_view: 'GnollHack wiki article',
        search_definitions: 'Definitions search',
        get_function_definition: 'Function definition',
        get_item_stats: 'Item stats',
        get_monster_stats: 'Monster stats',
        get_artifact_stats: 'Artifact stats',
        get_github_repo_info: 'GitHub repository info',
        search_github: 'GitHub search',
        delegate_to_subagent: 'Subagent',
        report_feedback: 'Feedback to developers'
      };
      for (const [name, label] of Object.entries(expected)) {
        expect(toolLabel(name)).toBe(label);
      }
    });

    it('reads an unknown tool in words', () => {
      expect(toolLabel('some_new_tool')).toBe('Some new tool');
      expect(toolLabel(' wiki_search ')).toBe('GnollHack wiki search');
      expect(toolLabel(null)).toBe('');
    });

    it('never reads an inherited property as a label', () => {
      expect(toolLabel('constructor')).toBe('Constructor');
      expect(toolLabel('__proto__')).toBe('Proto');
      expect(toolLabel('toString')).toBe('To string');
      expect(toolLabel('hasOwnProperty')).toBe('Has own property');
    });
  });

  describe('providerLabel', () => {
    it('spells the known providers their own way, whatever the stored case', () => {
      expect(providerLabel('openai')).toBe('OpenAI');
      expect(providerLabel('OpenAI')).toBe('OpenAI');
      expect(providerLabel('ANTHROPIC')).toBe('Anthropic');
      expect(providerLabel('google')).toBe('Google');
      expect(providerLabel('xai')).toBe('xAI');
      expect(providerLabel(' XAI ')).toBe('xAI');
    });

    it('shows any other provider as stored', () => {
      expect(providerLabel('Custom Endpoint')).toBe('Custom Endpoint');
      expect(providerLabel('mistral')).toBe('mistral');
      expect(providerLabel(null)).toBe('');
      expect(providerLabel('')).toBe('');
    });

    it('never reads an inherited property as a label', () => {
      expect(providerLabel('constructor')).toBe('constructor');
      expect(providerLabel('__proto__')).toBe('__proto__');
    });
  });
});
