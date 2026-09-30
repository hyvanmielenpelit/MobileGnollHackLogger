import type { BenchmarkModelComparisonDto, BenchmarkModelComparisonEntryDto } from './model-comparison.models';

/** `Model A`: the name an anonymized document gives the peer it letters `A`. */
export function anonymizedPeerName(letter: string): string {
  return `Model ${letter}`;
}

/** A pattern matching `text` case-insensitively, but never inside a longer word or model id. */
function wholeTextPattern(text: string): RegExp {
  const escaped = text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(?<![A-Za-z0-9])${escaped}(?![A-Za-z0-9])`, 'gi');
}

/** Every string in `value`, rewritten by `replacements`; objects and arrays are rebuilt, `skip` is kept as it is. */
function scrubStrings(value: unknown, replacements: readonly [RegExp, string][], skip: unknown): unknown {
  if (value === skip) {
    return value;
  }
  if (typeof value === 'string') {
    return replacements.reduce((text, [pattern, replacement]) => text.replace(pattern, replacement), value);
  }
  if (Array.isArray(value)) {
    return value.map(item => scrubStrings(item, replacements, skip));
  }
  if (value !== null && typeof value === 'object') {
    const result: Record<string, unknown> = {};
    for (const [key, item] of Object.entries(value)) {
      result[key] = scrubStrings(item, replacements, skip);
    }
    return result;
  }
  return value;
}

/**
 * The comparison as an anonymized document draws it: a deep copy in which every peer the document
 * letters is *Model X* (`letters`: entry key → letter, as the document's fact sheet assigns them)
 * with no provider and no model id, so every peer is drawn in the neutral gray; entries the document
 * does not letter are removed, and the subject is kept exactly as it is.
 *
 * Free text elsewhere in the copy that names a removed or lettered peer — its label, display name,
 * model id or source name, or a provider no other kept entry has — is rewritten, and the judge-family
 * diagnostics, which name models and providers throughout, are dropped: no chart reads them. Pure;
 * the input is never modified.
 */
export function anonymizeComparisonForSubject(
  dto: BenchmarkModelComparisonDto,
  subjectKey: string,
  letters: Readonly<Record<string, string>>
): BenchmarkModelComparisonDto {
  const copy = JSON.parse(JSON.stringify(dto)) as BenchmarkModelComparisonDto;
  const subject = copy.entries.find(entry => entry.key === subjectKey) ?? null;
  const subjectProvider = (subject?.provider ?? '').trim().toLowerCase();

  const names = new Map<string, string>();
  const providers = new Set<string>();
  const kept: BenchmarkModelComparisonEntryDto[] = [];
  for (const entry of copy.entries) {
    if (entry === subject) {
      kept.push(entry);
      continue;
    }
    const letter = letters[entry.key];
    const replacement = letter ? anonymizedPeerName(letter) : 'another model';
    for (const text of [entry.label, entry.modelDisplayName, entry.modelId, entry.sourceName]) {
      const trimmed = (text ?? '').trim();
      if (trimmed.length >= 2 && !names.has(trimmed.toLowerCase())) {
        names.set(trimmed.toLowerCase(), replacement);
      }
    }
    const provider = (entry.provider ?? '').trim();
    if (provider.length >= 2 && provider.toLowerCase() !== subjectProvider) {
      providers.add(provider);
    }
    if (!letter) {
      continue;
    }
    const name = anonymizedPeerName(letter);
    kept.push({
      ...entry,
      label: name,
      modelDisplayName: name,
      provider: '',
      modelId: '',
      sourceName: null,
      explanation: '',
      differences: []
    });
  }

  copy.entries = kept;
  const keptKeys = new Set(kept.map(entry => entry.key));
  copy.baselineEntryKeys = copy.baselineEntryKeys.filter(key => keptKeys.has(key));
  copy.excludedCount = kept.filter(entry => entry.excluded).length;
  copy.comparableCount = kept.length - copy.excludedCount;
  copy.panelDiagnostics = null;

  // Longest first, so a name that contains another is rewritten whole.
  const replacements: [RegExp, string][] = [
    ...[...names.entries()].sort((a, b) => b[0].length - a[0].length)
      .map(([text, replacement]): [RegExp, string] => [wholeTextPattern(text), replacement]),
    ...[...providers].sort((a, b) => b.length - a.length)
      .map((provider): [RegExp, string] => [wholeTextPattern(provider), 'another provider'])
  ];
  return scrubStrings(copy, replacements, subject) as BenchmarkModelComparisonDto;
}
