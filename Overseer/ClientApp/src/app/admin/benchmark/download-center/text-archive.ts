import { ZipEntryOptions, zipWriterModule } from '../model-comparison/figure-export';

/** One text file of an archive: its name, its content and the modification time it is stored with. */
export interface TextArchiveEntry {
  name: string;
  text: string;
  mtime: Date;
}

/** Deflate level for text: well compressed, and cheap enough for a synchronous zip on the main thread. */
const TEXT_DEFLATE_LEVEL = 6;

/**
 * Packs text files into one deflated zip, each entry stamped with its own `mtime` (without one,
 * `fflate` stamps `Date.now()`, and two downloads of the same files would differ byte for byte).
 *
 * Asynchronous only because `fflate` is loaded on first use. The zip itself is `zipSync`, never the
 * worker-backed `zip()`: the Content-Security-Policy refuses the blob-URL workers that path builds.
 * Names must be unique; see `uniqueFileNames`.
 */
export async function buildTextArchive(entries: readonly TextArchiveEntry[]): Promise<Blob> {
  const writer = await zipWriterModule.load();
  const encoder = new TextEncoder();
  const files: Record<string, [Uint8Array, ZipEntryOptions]> = {};
  for (const entry of entries) {
    files[entry.name] = [encoder.encode(entry.text), { level: TEXT_DEFLATE_LEVEL, mtime: entry.mtime }];
  }
  return new Blob([writer.zipSync(files) as unknown as BlobPart], { type: 'application/zip' });
}

/**
 * The names with repeats made unique by a `-2`, `-3`… before the extension, in order, so no
 * archive entry overwrites another. Case-insensitive, as the file systems the zip lands on are.
 */
export function uniqueFileNames(names: readonly string[]): string[] {
  const taken = new Set<string>();
  return names.map(name => {
    let candidate = name;
    let counter = 2;
    while (taken.has(candidate.toLowerCase())) {
      const dot = name.lastIndexOf('.');
      candidate = dot > 0 ? `${name.slice(0, dot)}-${counter}${name.slice(dot)}` : `${name}-${counter}`;
      counter++;
    }
    taken.add(candidate.toLowerCase());
    return candidate;
  });
}

/** One file as the manifest describes it. Null fields print as a dash. */
export interface ManifestFile {
  name: string;
  /** The exact text stored in the archive, hashed as UTF-8. */
  text: string;
  /** What the file is: `Executive Summary`, `Run report`, `Run diagnostics (captured now)`…. */
  description: string;
  documentId: number | null;
  audience: string | null;
  disclosure: string | null;
  naming: string | null;
  /** The renderer's `reportFormatVersion`. */
  rendererVersion: number | null;
  /** When the content was created, as an ISO-8601 UTC string. */
  createdAtUtc: string | null;
  writer: string | null;
  internalOnly: boolean;
}

/** A file that was chosen but could not be prepared. */
export interface ManifestFailure {
  /** What the file was, e.g. `Run report, run #42 (Markdown)`. */
  label: string;
  reason: string;
}

export interface ManifestInput {
  /** `Internal package`, `Provider package` or `Custom`. */
  packageName: string;
  /** The packaging time: printed here and in the zip name, nowhere else. */
  packagedAt: Date;
  files: readonly ManifestFile[];
  /** Listed under *Not included*; omitted when empty. */
  failures?: readonly ManifestFailure[];
}

export const MANIFEST_FILE_NAME = 'MANIFEST.md';

/** Why a hash is missing: the Web Crypto digest exists only in a secure context. */
export const SHA256_UNAVAILABLE_NOTE = 'SHA-256 not computed: the browser offers no crypto.subtle in this context (it requires HTTPS).';

/** The lower-case hex SHA-256 of the text's UTF-8 bytes, or null where `crypto.subtle` is unavailable. */
export async function sha256Hex(text: string): Promise<string | null> {
  const subtle = globalThis.crypto?.subtle;
  if (!subtle || typeof subtle.digest !== 'function') {
    return null;
  }
  try {
    const digest = await subtle.digest('SHA-256', new TextEncoder().encode(text));
    return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
  } catch {
    return null;
  }
}

/**
 * `MANIFEST.md`: when the package was made, then one block per file with its name, document id,
 * audience, disclosure, peer naming, renderer version, creation time, writer and SHA-256. The same
 * input always gives the same text.
 */
export async function buildManifest(input: ManifestInput): Promise<string> {
  const hashes = await Promise.all(input.files.map(file => sha256Hex(file.text)));
  const anyMissing = hashes.some(hash => hash === null);

  const lines: string[] = [
    '# Download Manifest',
    '',
    `- **Package:** ${input.packageName}`,
    `- **Packaged:** ${isoSeconds(input.packagedAt)}`,
    `- **Files:** ${input.files.length}`,
    ''
  ];
  if (input.files.some(file => file.internalOnly)) {
    lines.push('Files whose names end in `_INTERNAL` are for the Overseer team only. Do not share them outside it.', '');
  }
  if (anyMissing) {
    lines.push(`> ${SHA256_UNAVAILABLE_NOTE}`, '');
  }

  input.files.forEach((file, index) => {
    lines.push(
      `## ${index + 1}. \`${code(file.name)}\``,
      '',
      `- **Content:** ${file.description}`,
      `- **Document id:** ${dash(file.documentId)}`,
      `- **Audience:** ${dash(file.audience)}`,
      `- **Disclosure:** ${dash(file.disclosure)}`,
      `- **Peers:** ${dash(file.naming)}`,
      `- **Renderer version:** ${dash(file.rendererVersion)}`,
      `- **Created:** ${dash(file.createdAtUtc)}`,
      `- **Writer:** ${dash(file.writer)}`,
      `- **SHA-256:** ${hashes[index] ? `\`${hashes[index]}\`` : 'not computed'}`,
      ''
    );
  });

  if (input.failures && input.failures.length > 0) {
    lines.push('## Not included', '');
    for (const failure of input.failures) {
      lines.push(`- ${failure.label}: ${failure.reason}`);
    }
    lines.push('');
  }

  return lines.join('\n');
}

/** `2026-09-28T10:15:00Z`: UTC, whole seconds. */
function isoSeconds(date: Date): string {
  return date.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

function dash(value: string | number | null | undefined): string {
  return value === null || value === undefined || value === '' ? '—' : String(value);
}

/** A name inside a code span cannot itself close the span. */
function code(text: string): string {
  return text.replace(/`/g, "'");
}
