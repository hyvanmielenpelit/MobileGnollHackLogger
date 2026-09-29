/**
 * The client's reading of the server's report-writer rules, shared by the launcher's Report Writer
 * and the run report's AI Reports tab. It gives an instant answer; the server stays authoritative.
 *
 * Refused: a configuration the server would not run, and the model under test itself. Warned: a
 * writer from the model under test's provider, which the operator may acknowledge per request.
 */

/** A report writer as the rules compare it: a system AI configuration, or the fields of one. */
export interface ReportWriterModel {
  id?: number | null;
  displayName?: string | null;
  provider?: string | null;
  modelId?: string | null;
  isEnabled?: boolean | null;
  hasApiKey?: boolean | null;
  modelRole?: number | null;
}

/** The model under test, by the configuration that ran or will run it. */
export interface ReportWriterCandidate {
  id?: number | null;
  provider?: string | null;
  modelId?: string | null;
}

/** The server's `InvalidWriterMessage`, word for word. */
export const REPORT_WRITER_INVALID_MESSAGE =
  'Report writer configuration is invalid, disabled, missing an API key, or not configured with the Benchmark role.';

/** The server's `ModelUnderTestMessage`, word for word. */
export const REPORT_WRITER_MODEL_UNDER_TEST_MESSAGE = 'The model under test cannot write its own reports.';

/** The Benchmark bit of a configuration's model role. */
const BENCHMARK_ROLE = 4;

function normalized(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

/** "Family" is the provider, compared the way the server's IsSameProvider compares it. */
export function isSameProvider(a: string | null | undefined, b: string | null | undefined): boolean {
  return normalized(a) !== '' && normalized(a) === normalized(b);
}

/** Why the server would refuse this writer for this candidate, or '' when it would accept it. */
export function reportWriterRefusal(
  writer: ReportWriterModel | null | undefined,
  candidate: ReportWriterCandidate | null | undefined
): string {
  if (!writer) {
    return '';
  }
  if (writer.isEnabled === false || writer.hasApiKey === false ||
    (writer.modelRole != null && (writer.modelRole & BENCHMARK_ROLE) !== BENCHMARK_ROLE)) {
    return REPORT_WRITER_INVALID_MESSAGE;
  }
  if (!candidate) {
    return '';
  }
  const sameConfiguration = writer.id != null && writer.id === candidate.id;
  const sameModel = isSameProvider(writer.provider, candidate.provider) &&
    normalized(writer.modelId) !== '' && normalized(writer.modelId) === normalized(candidate.modelId);
  return sameConfiguration || sameModel ? REPORT_WRITER_MODEL_UNDER_TEST_MESSAGE : '';
}

/** The same-provider warning's sentence, for a writer and its provider as the server or the client names them. */
export function reportWriterWarningText(writerName: string | null | undefined, provider: string | null | undefined): string {
  const name = writerName?.trim() || 'This report writer';
  const family = provider?.trim() || 'the same provider';
  return `${name} is from ${family}, the provider of the model under test. Its reports may describe that model more favorably.`;
}

/**
 * The warning a writer from the model under test's provider carries, or '' when there is none. A
 * refused writer carries no warning: the refusal is the one thing to say about it.
 */
export function reportWriterWarning(
  writer: ReportWriterModel | null | undefined,
  candidate: ReportWriterCandidate | null | undefined
): string {
  if (!writer || !candidate || reportWriterRefusal(writer, candidate) !== '') {
    return '';
  }
  return isSameProvider(writer.provider, candidate.provider)
    ? reportWriterWarningText(writer.displayName, writer.provider)
    : '';
}
