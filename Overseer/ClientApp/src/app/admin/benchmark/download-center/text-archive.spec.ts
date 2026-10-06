import { unzipSync } from 'fflate';

import {
  ArchiveEntry,
  MANIFEST_FILE_NAME,
  ManifestFile,
  SHA256_UNAVAILABLE_NOTE,
  buildManifest,
  buildTextArchive,
  sha256Hex,
  uniqueFileNames
} from './text-archive';
import { zipEntryTimes } from './zip-entry-times.testing';

/** Each entry's compression method from the zip's central directory: 0 stored, 8 deflated. */
function zipEntryMethods(zip: Uint8Array): Map<string, number> {
  const view = new DataView(zip.buffer, zip.byteOffset, zip.byteLength);
  let end = zip.byteLength - 22;
  while (end >= 0 && view.getUint32(end, true) !== 0x06054b50) {
    end--;
  }
  const count = view.getUint16(end + 10, true);
  let offset = view.getUint32(end + 16, true);
  const methods = new Map<string, number>();
  for (let i = 0; i < count; i++) {
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const name = new TextDecoder().decode(zip.subarray(offset + 46, offset + 46 + nameLength));
    methods.set(name, view.getUint16(offset + 10, true));
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return methods;
}

describe('text-archive', () => {
  const created = new Date(2026, 8, 20, 9, 30, 12);
  const completed = new Date(2026, 8, 21, 17, 5, 44);
  const packaged = new Date(2026, 8, 28, 10, 15, 2);

  const entries = [
    { name: 'report_summary_anonymized.md', text: '# Executive Summary\n\nCost $4 per question.\n', mtime: created },
    { name: 'Suite_Model_20260921_170500_INTERNAL.md', text: '# Run report\n\nÄäkköset ja €.\n', mtime: completed },
    { name: MANIFEST_FILE_NAME, text: '# Download Manifest\n', mtime: packaged }
  ];

  describe('buildTextArchive', () => {
    it('stores every entry under its name with its text', async () => {
      const archive = await buildTextArchive(entries);

      expect(archive.type).toBe('application/zip');
      const unzipped = unzipSync(new Uint8Array(await archive.arrayBuffer()));
      expect(Object.keys(unzipped).sort()).toEqual(entries.map(entry => entry.name).sort());
      for (const entry of entries) {
        expect(new TextDecoder().decode(unzipped[entry.name])).toBe(entry.text);
      }
    });

    it('stamps every entry with its stated mtime', async () => {
      const archive = await buildTextArchive(entries);

      const times = zipEntryTimes(new Uint8Array(await archive.arrayBuffer()));
      for (const entry of entries) {
        expect(times.get(entry.name)?.getTime()).toBe(entry.mtime.getTime());
      }
    });

    it('gives identical bytes for identical input', async () => {
      const first = new Uint8Array(await (await buildTextArchive(entries)).arrayBuffer());
      const second = new Uint8Array(await (await buildTextArchive(entries)).arrayBuffer());

      expect(Array.from(second)).toEqual(Array.from(first));
    });

    it('deflates text rather than storing it', async () => {
      const text = 'The same line of a long report.\n'.repeat(400);
      const archive = await buildTextArchive([{ name: 'long.md', text, mtime: created }]);

      expect(archive.size).toBeLessThan(text.length / 4);
    });

    it('stores bytes uncompressed and exactly, beside deflated text', async () => {
      const pdf = new Uint8Array(4096);
      pdf.set(new TextEncoder().encode('%PDF-1.7\n'));
      for (let i = 9; i < pdf.length; i++) {
        pdf[i] = i % 7;
      }
      const docx = new Uint8Array(pdf.length);
      docx.set(new TextEncoder().encode('PK\u0003\u0004'));
      docx.set(pdf.subarray(9), 9);
      const mixed: ArchiveEntry[] = [
        { name: 'report_INTERNAL.pdf', bytes: pdf, mtime: completed },
        { name: 'report_INTERNAL.docx', bytes: docx, mtime: completed },
        { name: 'report_INTERNAL.md', text: 'The same line of a long report.\n'.repeat(100), mtime: completed }
      ];

      const zip = new Uint8Array(await (await buildTextArchive(mixed)).arrayBuffer());

      const methods = zipEntryMethods(zip);
      expect(methods.get('report_INTERNAL.pdf')).toBe(0);
      expect(methods.get('report_INTERNAL.docx')).toBe(0);
      expect(methods.get('report_INTERNAL.md')).toBe(8);
      const unzipped = unzipSync(zip);
      expect(Array.from(unzipped['report_INTERNAL.pdf'])).toEqual(Array.from(pdf));
      expect(Array.from(unzipped['report_INTERNAL.docx'])).toEqual(Array.from(docx));
      expect(zipEntryTimes(zip).get('report_INTERNAL.pdf')?.getTime()).toBe(completed.getTime());
    });
  });

  describe('uniqueFileNames', () => {
    it('numbers repeats before the extension, ignoring case', () => {
      expect(uniqueFileNames(['a.md', 'b.md', 'A.md', 'a.md', 'noext', 'noext']))
        .toEqual(['a.md', 'b.md', 'A-2.md', 'a-3.md', 'noext', 'noext-2']);
    });
  });

  describe('sha256Hex', () => {
    it('hashes the UTF-8 bytes', async () => {
      expect(await sha256Hex('abc')).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
    });

    it('hashes bytes as they are', async () => {
      expect(await sha256Hex(new Uint8Array([0x61, 0x62, 0x63]))).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
    });
  });

  describe('buildManifest', () => {
    const files: ManifestFile[] = [
      {
        name: 'gpt-model-executive-summary_summary_anonymized.html',
        content: '<html>summary</html>',
        description: 'Executive Summary',
        format: 'HTML',
        pdfPaper: null,
        wordPaper: null,
        documentId: 12,
        audience: 'Executive Summary',
        disclosure: 'Summary',
        naming: 'Anonymized',
        rendererVersion: 3,
        createdAtUtc: '2026-09-20T09:30:12Z',
        writer: 'Claude Opus',
        internalOnly: false
      },
      {
        name: 'Suite_Model_20260921_170500_INTERNAL.md',
        content: 'abc',
        description: 'Run report',
        format: 'Markdown',
        pdfPaper: null,
        wordPaper: null,
        documentId: null,
        audience: null,
        disclosure: null,
        naming: null,
        rendererVersion: null,
        createdAtUtc: '2026-09-21T17:05:44Z',
        writer: null,
        internalOnly: true
      }
    ];

    it('lists every file with its fields and hash, and the packaging time once', async () => {
      const manifest = await buildManifest({ packageName: 'Internal package', packagedAt: new Date(Date.UTC(2026, 8, 28, 10, 15, 2, 345)), files });

      expect(manifest).toContain('- **Package:** Internal package');
      expect(manifest).toContain('- **Packaged:** 2026-09-28T10:15:02Z');
      expect(manifest.match(/2026-09-28T10:15:02Z/g)?.length).toBe(1);
      expect(manifest).toContain('- **Files:** 2');
      expect(manifest).toContain('## 1. `gpt-model-executive-summary_summary_anonymized.html`');
      expect(manifest).toContain('- **Document id:** 12');
      expect(manifest).toContain('- **Audience:** Executive Summary');
      expect(manifest).toContain('- **Disclosure:** Summary');
      expect(manifest).toContain('- **Peers:** Anonymized');
      expect(manifest).toContain('- **Renderer version:** 3');
      expect(manifest).toContain('- **Created:** 2026-09-20T09:30:12Z');
      expect(manifest).toContain('- **Writer:** Claude Opus');
      expect(manifest).toContain('## 2. `Suite_Model_20260921_170500_INTERNAL.md`');
      expect(manifest).toContain('- **Document id:** —');
      expect(manifest).toContain('- **SHA-256:** `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`');
      expect(manifest).toContain('_INTERNAL');
      expect(manifest).not.toContain(SHA256_UNAVAILABLE_NOTE);
    });

    it('names every file’s format, and a PDF’s conformance and paper, hashing its bytes', async () => {
      const pdfFile: ManifestFile = {
        ...files[1],
        name: 'Suite_Model_20260921_170500_INTERNAL.pdf',
        content: new Uint8Array([0x61, 0x62, 0x63]),
        format: 'PDF',
        pdfPaper: 'letter'
      };
      const manifest = await buildManifest({ packageName: 'Internal package', packagedAt: packaged, files: [files[0], pdfFile] });

      const blocks = manifest.split('\n## ').slice(1);
      expect(blocks[0]).toContain('- **Content:** Executive Summary\n- **Format:** HTML\n- **Document id:** 12');
      expect(blocks[0]).not.toContain('**PDF:**');
      expect(blocks[1]).toContain('- **Content:** Run report\n- **Format:** PDF\n- **PDF:** PDF/UA-1, PDF/A-3A, US Letter\n');
      expect(blocks[1]).toContain('- **SHA-256:** `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`');

      const a4 = await buildManifest({ packageName: 'Custom', packagedAt: packaged, files: [{ ...pdfFile, pdfPaper: 'a4' }] });
      expect(a4).toContain('- **PDF:** PDF/UA-1, PDF/A-3A, A4\n');
      expect(a4).not.toContain('**Word:**');
    });

    it('names a Word document’s format and paper', async () => {
      const wordFile: ManifestFile = {
        ...files[1],
        name: 'Suite_Model_20260921_170500_INTERNAL.docx',
        content: new Uint8Array([0x61, 0x62, 0x63]),
        format: 'Word',
        wordPaper: 'letter'
      };
      const manifest = await buildManifest({ packageName: 'Internal package', packagedAt: packaged, files: [wordFile] });

      expect(manifest).toContain('- **Content:** Run report\n- **Format:** Word\n- **Word:** Office Open XML (.docx), US Letter\n');
      expect(manifest).not.toContain('**PDF:**');
      expect(manifest).toContain('- **SHA-256:** `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`');

      const a4 = await buildManifest({ packageName: 'Custom', packagedAt: packaged, files: [{ ...wordFile, wordPaper: 'a4' }] });
      expect(a4).toContain('- **Word:** Office Open XML (.docx), A4\n');
    });

    it('prints a Format line and no PDF or Word line for a download without either', async () => {
      const manifest = await buildManifest({ packageName: 'Custom', packagedAt: packaged, files });

      expect(manifest.match(/- \*\*Format:\*\* /g)?.length).toBe(2);
      expect(manifest).toContain('- **Format:** Markdown');
      expect(manifest).not.toContain('PDF/UA');
      expect(manifest).not.toContain('Office Open XML');
    });

    it('lists the comparisons in the header, and each file\'s comparison and models, a dash where it has none', async () => {
      const numbered: ManifestFile = {
        ...files[0],
        comparison: 'Comparison #12 — Five-model comparison',
        models: { label: 'Models', text: 'GPT-5.6 Luna, Grok 5' }
      };
      const one = await buildManifest({
        packageName: 'Internal package', packagedAt: packaged, comparisons: ['Comparison #12 — Five-model comparison'], files: [numbered, files[1]]
      });

      expect(one).toContain('- **Files:** 2\n- **Comparison:** Comparison #12 — Five-model comparison\n\n');
      const blocks = one.split('\n## ').slice(1);
      expect(blocks[0]).toContain('- **Audience:** Executive Summary\n- **Comparison:** Comparison #12 — Five-model comparison\n'
        + '- **Models:** GPT-5.6 Luna, Grok 5\n- **Disclosure:** Summary\n');
      expect(blocks[1]).toContain('- **Audience:** —\n- **Comparison:** —\n- **Model:** —\n');

      const several = await buildManifest({
        packageName: 'Custom', packagedAt: packaged, comparisons: ['Comparison #12', 'Comparison #14 — Second comparison'], files
      });
      expect(several).toContain('- **Comparisons:** Comparison #12; Comparison #14 — Second comparison\n');
      expect(several).not.toContain('- **Comparison:** Comparison');

      const none = await buildManifest({ packageName: 'Custom', packagedAt: packaged, comparisons: [], files });
      expect(none).toContain('- **Files:** 2\n\n');
      expect(none).not.toContain('**Comparisons:**');
    });

    it('lists the files that could not be prepared, only when there are any', async () => {
      const failures = [{ label: 'Run report, run #42 (Markdown)', reason: 'the run no longer exists' }];
      const withFailures = await buildManifest({ packageName: 'Custom', packagedAt: packaged, files, failures });
      const without = await buildManifest({ packageName: 'Custom', packagedAt: packaged, files, failures: [] });

      expect(withFailures).toContain('## Not included\n\n- Run report, run #42 (Markdown): the run no longer exists');
      expect(without).not.toContain('Not included');
    });

    it('gives identical text for identical input', async () => {
      const input = { packageName: 'Custom', packagedAt: packaged, files };
      expect(await buildManifest(input)).toBe(await buildManifest(input));
    });

    it('omits the hashes with a note where crypto.subtle is unavailable', async () => {
      const own = Object.getOwnPropertyDescriptor(globalThis, 'crypto');
      Object.defineProperty(globalThis, 'crypto', { value: {}, configurable: true });
      try {
        const manifest = await buildManifest({ packageName: 'Custom', packagedAt: packaged, files });

        expect(manifest).toContain(SHA256_UNAVAILABLE_NOTE);
        expect(manifest).toContain('- **SHA-256:** not computed');
        expect(manifest).not.toContain('ba7816bf');
      } finally {
        if (own) {
          Object.defineProperty(globalThis, 'crypto', own);
        } else {
          delete (globalThis as { crypto?: unknown }).crypto;
        }
      }
    });
  });
});
