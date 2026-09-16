import { fakeAsync, tick } from '@angular/core/testing';
import { downloadTextFile, safeFileName } from './download.util';

describe('download.util', () => {
  describe('safeFileName', () => {
    it('lower-cases, maps whitespace to dashes and strips everything else', () => {
      expect(safeFileName('My Suite: "Advanced" v2.yaml')).toBe('my-suite-advanced-v2.yaml');
    });

    it('collapses repeats and trims leading and trailing separators', () => {
      expect(safeFileName('  --a   b..yaml-- ')).toBe('a-b.yaml');
      expect(safeFileName('../../etc/passwd')).toBe('etcpasswd');
    });

    it('defaults to export', () => {
      expect(safeFileName('')).toBe('export');
      expect(safeFileName('???')).toBe('export');
    });
  });

  describe('downloadTextFile', () => {
    it('clicks a download anchor and revokes the object URL', fakeAsync(() => {
      const create = spyOn(URL, 'createObjectURL').and.returnValue('blob:test-url');
      const revoke = spyOn(URL, 'revokeObjectURL');
      const click = spyOn(HTMLAnchorElement.prototype, 'click');

      downloadTextFile('questions.yaml', 'format: x\n');

      expect(create).toHaveBeenCalledTimes(1);
      const blob = create.calls.mostRecent().args[0] as Blob;
      expect(blob.type).toBe('application/yaml;charset=utf-8');
      expect(click).toHaveBeenCalledTimes(1);
      expect(revoke).not.toHaveBeenCalled();

      tick(0);
      expect(revoke).toHaveBeenCalledWith('blob:test-url');
      expect(document.querySelector('a[download="questions.yaml"]')).toBeNull();
    }));
  });
});
