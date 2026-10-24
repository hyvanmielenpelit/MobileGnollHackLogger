import {
  BenchmarkExperimentalChangeCatalogDto,
  BenchmarkExperimentalChangeDto
} from '../../../services/admin-benchmark.service';

/** One change of the catalog, a built-in text-only one unless the overrides say otherwise. */
export function buildChange(overrides: Partial<BenchmarkExperimentalChangeDto> & { id: string }): BenchmarkExperimentalChangeDto {
  return {
    title: overrides.id,
    summary: `${overrides.id} summary.`,
    expectedEffect: null,
    source: 'builtIn',
    kinds: [],
    hashPrefix: '0123456789ab',
    isValid: true,
    invalidReason: null,
    retired: false,
    locked: false,
    createdAtUtc: null,
    updatedAtUtc: null,
    copiedFrom: null,
    experimentalTool: null,
    experiment: null,
    descriptionEdits: [],
    instructions: null,
    appliesUnder: { production: 'full', sourceOnly: 'full', closedBook: 'none' },
    notApplyingUnderSourceOnly: [],
    mentions: [],
    conflictsWith: [],
    runCount: 0,
    canEdit: false,
    canEditText: false,
    canDelete: false,
    canCopy: false,
    ...overrides
  };
}

/** The live wiki_search description the fixtures edit. */
export const LIVE_WIKI_SEARCH = 'Searches the GnollHack wiki.\nReturns page titles.';

/** The four built-in changes. */
export function raceRoleTool(overrides: Partial<BenchmarkExperimentalChangeDto> = {}): BenchmarkExperimentalChangeDto {
  return buildChange({
    id: 'race-role-tool-v1',
    title: 'Race and role lookup tool',
    summary: 'Adds get_character_info, which reads a race\'s or role\'s rules straight from the game tables.',
    expectedEffect: 'Questions that depend on the player\'s race or role are answered from the tables, not from memory.',
    kinds: ['newTool'],
    hashPrefix: 'aaaaaaaaaaaa',
    experimentalTool: {
      name: 'get_character_info',
      description: 'Looks up a race or a role.',
      parameterSchemaJson: JSON.stringify({
        type: 'object',
        properties: { race: { type: 'string', description: 'A race name.' }, role: { type: 'string', description: 'A role name.' } },
        required: ['race']
      })
    },
    runCount: 3,
    ...overrides
  });
}

export function statsReferencedBy(overrides: Partial<BenchmarkExperimentalChangeDto> = {}): BenchmarkExperimentalChangeDto {
  return buildChange({
    id: 'stats-referenced-by-v1',
    title: 'Where the game code uses it',
    summary: 'The item, monster and artifact lookups also list the game functions that use the thing.',
    kinds: ['toolOutput', 'toolDescription'],
    hashPrefix: 'bbbbbbbbbbbb',
    experiment: 'stats_referenced_by',
    descriptionEdits: [
      { tool: 'get_item_stats', mode: 'add', text: 'Read the function referenced_by names.', productionText: 'Item stats.' }
    ],
    ...overrides
  });
}

export function askerSearchWords(overrides: Partial<BenchmarkExperimentalChangeDto> = {}): BenchmarkExperimentalChangeDto {
  return buildChange({
    id: 'asker-search-words-v1',
    title: 'Search with the player\'s race and role',
    summary: 'Adds one sentence to the wiki_search description.',
    kinds: ['toolDescription'],
    hashPrefix: 'cccccccccccc',
    descriptionEdits: [
      { tool: 'wiki_search', mode: 'add', text: 'Put the race and role in the query.', productionText: LIVE_WIKI_SEARCH }
    ],
    appliesUnder: { production: 'full', sourceOnly: 'none', closedBook: 'none' },
    notApplyingUnderSourceOnly: ['wiki_search'],
    ...overrides
  });
}

export function askerCharacterProcedure(overrides: Partial<BenchmarkExperimentalChangeDto> = {}): BenchmarkExperimentalChangeDto {
  return buildChange({
    id: 'asker-character-procedure-v1',
    title: 'Answer from the player\'s own character',
    summary: 'Adds a paragraph to the end of the AI\'s instructions.',
    kinds: ['instructions'],
    hashPrefix: 'dddddddddddd',
    instructions: 'When the question names a race, read get_character_info.',
    appliesUnder: { production: 'full', sourceOnly: 'full', closedBook: 'full' },
    mentions: ['race-role-tool-v1'],
    ...overrides
  });
}

/** A change written in Overseer that replaces the wiki_search description, so it conflicts with the addition. */
export function citePageFirst(overrides: Partial<BenchmarkExperimentalChangeDto> = {}): BenchmarkExperimentalChangeDto {
  return buildChange({
    id: 'cite-page-first-v1',
    title: 'Cite the page first',
    summary: 'Rewrites the wiki_search description to ask for the page before the answer.',
    source: 'database',
    kinds: ['toolDescription'],
    hashPrefix: 'eeeeeeeeeeee',
    createdAtUtc: '2026-10-24T09:00:00Z',
    updatedAtUtc: '2026-10-24T10:00:00Z',
    descriptionEdits: [
      { tool: 'wiki_search', mode: 'replace', text: `${LIVE_WIKI_SEARCH}\nCite the page first.`, productionText: LIVE_WIKI_SEARCH }
    ],
    appliesUnder: { production: 'full', sourceOnly: 'none', closedBook: 'none' },
    notApplyingUnderSourceOnly: ['wiki_search'],
    conflictsWith: ['asker-search-words-v1'],
    canEdit: true,
    canEditText: true,
    canDelete: true,
    canCopy: true,
    ...overrides
  });
}

/** The four built-in changes; the search-words change lists its conflict with the database one. */
export function builtInChanges(): BenchmarkExperimentalChangeDto[] {
  return [
    raceRoleTool(),
    statsReferencedBy(),
    askerSearchWords({ conflictsWith: ['cite-page-first-v1'] }),
    askerCharacterProcedure()
  ];
}

/** A catalog of the changes, with the editor's tools and limits. */
export function buildCatalog(
  changes: BenchmarkExperimentalChangeDto[] = [...builtInChanges(), citePageFirst()],
  overrides: Partial<BenchmarkExperimentalChangeCatalogDto> = {}): BenchmarkExperimentalChangeCatalogDto {
  return {
    changes,
    recentSelections: [],
    productionTools: [
      { name: 'wiki_search', description: LIVE_WIKI_SEARCH, withheldUnderSourceOnly: true },
      { name: 'source_code_search', description: 'Searches the source code.', withheldUnderSourceOnly: false }
    ],
    limits: { titleMax: 60, summaryMax: 220, textMax: 4000, descriptionEditsMax: 6 },
    maxStampLength: 160,
    ...overrides
  };
}
