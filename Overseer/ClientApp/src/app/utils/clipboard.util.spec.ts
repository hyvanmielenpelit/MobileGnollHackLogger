import { copyTextFromPromise, copyToClipboard } from './clipboard.util';

/**
 * `navigator.clipboard` is a read-only accessor on the real navigator, so each case installs its
 * own descriptor and restores the original afterwards. Without the restore, a suite that ran the
 * missing-API case would leave every later spec in this browser without a clipboard.
 */
describe('clipboard.util', () => {
  const original = Object.getOwnPropertyDescriptor(Navigator.prototype, 'clipboard')
    ?? Object.getOwnPropertyDescriptor(navigator, 'clipboard');

  function installClipboard(value: unknown): void {
    Object.defineProperty(navigator, 'clipboard', {
      value,
      configurable: true,
      writable: true
    });
  }

  afterEach(() => {
    delete (navigator as { clipboard?: unknown }).clipboard;
    if (original) {
      Object.defineProperty(navigator, 'clipboard', original);
    }
  });

  it('writes the text and reports success', async () => {
    const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    installClipboard({ writeText });

    await expectAsync(copyToClipboard('the methods statement')).toBeResolvedTo(true);
    expect(writeText).toHaveBeenCalledWith('the methods statement');
  });

  it('reports failure rather than throwing when there is no clipboard API', async () => {
    // What an insecure context looks like from script: the property is simply not there.
    installClipboard(undefined);

    await expectAsync(copyToClipboard('anything')).toBeResolvedTo(false);
  });

  it('reports failure when the write is rejected', async () => {
    const writeText = jasmine.createSpy('writeText')
      .and.returnValue(Promise.reject(new Error('Write permission denied.')));
    installClipboard({ writeText });

    // Resolved false, never a rejection: a denied permission is an outcome the caller renders.
    await expectAsync(copyToClipboard('anything')).toBeResolvedTo(false);
    expect(writeText).toHaveBeenCalled();
  });

  describe('copyTextFromPromise', () => {
    it('falls back to writeText once the text resolves when there is no write()', async () => {
      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      installClipboard({ writeText });

      await expectAsync(copyTextFromPromise(Promise.resolve('late text'))).toBeResolvedTo(true);
      expect(writeText).toHaveBeenCalledWith('late text');
    });

    it('reports failure when the text never arrives', async () => {
      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      installClipboard({ writeText });

      await expectAsync(copyTextFromPromise(Promise.reject(new Error('fetch failed')))).toBeResolvedTo(false);
      expect(writeText).not.toHaveBeenCalled();
    });

    it('reports failure when there is no clipboard API', async () => {
      installClipboard(undefined);
      await expectAsync(copyTextFromPromise(Promise.resolve('x'))).toBeResolvedTo(false);
    });

    it('writes a promised ClipboardItem synchronously where the platform supports it', async () => {
      if (typeof ClipboardItem === 'undefined') {
        pending('ClipboardItem is not available in this browser.');
        return;
      }
      let written: ClipboardItem[] | null = null;
      const write = jasmine.createSpy('write').and.callFake((items: ClipboardItem[]) => {
        written = items;
        return Promise.resolve();
      });
      installClipboard({ write, writeText: jasmine.createSpy('writeText') });

      let resolveText!: (t: string) => void;
      const pendingText = new Promise<string>(r => { resolveText = r; });
      const copy = copyTextFromPromise(pendingText);

      // Issued before the text exists, which is what keeps the write inside the user's activation.
      expect(write).toHaveBeenCalledTimes(1);
      resolveText('produced later');
      await expectAsync(copy).toBeResolvedTo(true);

      const blob = await written![0].getType('text/plain');
      expect(await blob.text()).toBe('produced later');
    });
  });
});
