import { unzipSync } from 'fflate';

import {
  MANIFEST_FILE_NAME,
  ManifestFile,
  SHA256_UNAVAILABLE_NOTE,
  buildManifest,
  buildTextArchive,
  sha256Hex,
  uniqueFileNames
} from './text-archive';
import { zipEntryTimes } from './zip-entry-times.testing';

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
  });

  describe('buildManifest', () => {
    const files: ManifestFile[] = [
      {
        name: 'gpt-model-executive-summary_summary_anonymized.html',
        text: '<html>summary</html>',
        description: 'Executive Summary',
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
        text: 'abc',
        description: 'Run report',
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
