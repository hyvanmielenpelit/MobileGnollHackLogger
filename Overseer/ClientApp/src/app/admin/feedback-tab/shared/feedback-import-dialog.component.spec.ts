import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import {
  FeedbackResolutionPreview,
  FeedbackResolutionPreviewItem,
  FeedbackTransferService
} from '../../../services/feedback-transfer.service';
import {
  FeedbackImportDialogComponent,
  feedbackImportBadges,
  feedbackIssueHeading,
  feedbackStatusLabel
} from './feedback-import-dialog.component';

const FP = '3f2a9c1b0d4e'.padEnd(64, 'a');
const FP2 = 'b'.repeat(64);

const header = 'format: overseer-feedback-resolutions\nversion: 1\nsource: overseer\nexportedAtUtc: "2026-10-23T12:34:56Z"\n';
const validFile = header + [
  'issues:',
  '  - id: 42',
  `    fingerprint: "${FP}"`,
  '    status: resolved',
  '    resolution: |',
  '      Fixed the wiki search.',
  '  - id: 7',
  `    fingerprint: "${FP2}"`,
  ''
].join('\n');

function previewItem(overrides: Partial<FeedbackResolutionPreviewItem> = {}): FeedbackResolutionPreviewItem {
  return {
    index: 0,
    id: 42,
    fingerprint: FP,
    exists: true,
    fingerprintMatches: true,
    category: 'tool_problem',
    subject: 'wiki_search finds <b>nothing</b>',
    current: { status: 'new', note: null, resolution: null, fixedInVersion: null, triagedUtc: null, lastSeenUtc: '2026-10-22T10:00:00Z' },
    after: { status: 'resolved', note: null, resolution: 'Fixed the wiki search.', fixedInVersion: null },
    changes: { status: true, note: false, resolution: true, fixedInVersion: false },
    hasChanges: true,
    occurrencesSinceExport: 0,
    triagedSinceExport: false,
    regressedSinceExport: false,
    errors: [],
    notices: [],
    ...overrides
  };
}

function preview(items: FeedbackResolutionPreviewItem[], overrides: Partial<FeedbackResolutionPreview> = {}): FeedbackResolutionPreview {
  return {
    source: 'overseer',
    exportedAtUtc: '2026-10-23T12:34:56Z',
    errorCount: items.filter(i => i.errors.length > 0).length,
    changeCount: items.filter(i => i.hasChanges).length,
    triagedSinceExportCount: items.filter(i => i.triagedSinceExport).length,
    notices: [],
    items,
    ...overrides
  };
}

const unchangedSeven = (): FeedbackResolutionPreviewItem => previewItem({
  index: 1, id: 7, fingerprint: FP2, subject: 'Another issue',
  current: { status: 'acknowledged', note: 'Looking.', resolution: null, fixedInVersion: null, triagedUtc: '2026-10-20T09:00:00Z', lastSeenUtc: null },
  after: { status: 'acknowledged', note: 'Looking.', resolution: null, fixedInVersion: null },
  changes: { status: false, note: false, resolution: false, fixedInVersion: false },
  hasChanges: false
});

describe('FeedbackImportDialogComponent', () => {
  describe('labels', () => {
    it('names statuses and issues', () => {
      expect(feedbackStatusLabel('resolved')).toBe('Resolved');
      expect(feedbackStatusLabel('regressed')).toBe('Regressed');
      expect(feedbackStatusLabel(null)).toBe('(none)');
      expect(feedbackIssueHeading('overseer', 42, FP)).toBe('Issue #42');
      expect(feedbackIssueHeading('gnollbench', null, FP)).toBe('Issue 3f2a9c1b0d4e');
    });

    it('badges what an item changes and what happened since the export', () => {
      expect(feedbackImportBadges(previewItem()).map(b => b.text)).toEqual(['status New → Resolved', 'resolution']);
      expect(feedbackImportBadges(unchangedSeven()).map(b => b.text)).toEqual(['no changes']);
      expect(feedbackImportBadges(previewItem({
        changes: { status: false, note: true, resolution: false, fixedInVersion: true },
        triagedSinceExport: true, occurrencesSinceExport: 3, regressedSinceExport: true
      })).map(b => b.text)).toEqual(['note', 'fixed in version', 'Triaged since export', '3 new occurrences since export', 'Regressed since export']);
      expect(feedbackImportBadges(previewItem({ errors: ['issues[0] (id 42): no Overseer feedback issue has id 42.'] })).map(b => b.text))
        .toEqual(['cannot be applied']);
    });
  });

  describe('the dialog', () => {
    let fixture: ComponentFixture<FeedbackImportDialogComponent>;
    let component: FeedbackImportDialogComponent;
    let host: HTMLElement;
    let previewResolutions: Mock;
    let applyResolutions: Mock;
    let imported: Mock;

    const q = <T extends Element = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
    const all = (selector: string): HTMLElement[] => Array.from(host.querySelectorAll<HTMLElement>(selector));
    const text = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

    function footerButton(label: string): HTMLButtonElement {
      const found = all('.dialog-footer button').find(b => text(b) === label) as HTMLButtonElement | undefined;
      if (!found) throw new Error(`No footer button "${label}"`);
      return found;
    }

    async function paste(value: string): Promise<void> {
      await fixture.whenStable();
      const textarea = q<HTMLTextAreaElement>('textarea')!;
      textarea.value = value;
      textarea.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    }

    async function toReview(file = validFile): Promise<void> {
      await paste(file);
      await component.review();
      fixture.detectChanges();
    }

    beforeEach(async () => {
      previewResolutions = vi.fn().mockName('FeedbackTransferService.previewResolutions');
      applyResolutions = vi.fn().mockName('FeedbackTransferService.applyResolutions');
      await TestBed.configureTestingModule({
        imports: [FeedbackImportDialogComponent],
        providers: [{ provide: FeedbackTransferService, useValue: { previewResolutions, applyResolutions } }]
      }).compileComponents();

      fixture = TestBed.createComponent(FeedbackImportDialogComponent);
      component = fixture.componentInstance;
      host = fixture.nativeElement as HTMLElement;
      fixture.componentRef.setInput('source', 'overseer');
      imported = vi.fn().mockName('imported');
      component.imported.subscribe(imported);
      fixture.detectChanges();
      component.open();
    });

    afterEach(async () => {
      const dialog = q<HTMLDialogElement>('dialog');
      if (dialog?.open) {
        const closed = new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
        dialog.close();
        await closed;
      }
    });

    it('opens on the first step for its sub-tab', () => {
      expect(q<HTMLDialogElement>('dialog')!.open).toBe(true);
      expect(text(q('h3'))).toBe('Import resolutions into the Overseer sub-tab');
      expect(text(q('.gh-steps li[aria-current="step"]'))).toContain('Provide YAML');
      expect(footerButton('Validate').getAttribute('aria-disabled')).toBe('true');
    });

    it('validates in the browser and lists errors without asking the server', async () => {
      await paste(validFile.replace('source: overseer', 'source: gnollbench'));
      expect(footerButton('Validate').getAttribute('aria-disabled')).toBeNull();

      expect(await component.validate()).toBe(false);
      fixture.detectChanges();
      expect(all('.fi-errors li').map(text)).toContain('This file is for the GnollBench sub-tab.');

      await component.review();
      expect(previewResolutions).not.toHaveBeenCalled();
    });

    it('shows the summary and notices of a valid file', async () => {
      await paste(validFile);
      expect(await component.validate()).toBe(true);
      fixture.detectChanges();
      expect(text(q('.fi-valid-summary'))).toBe('Valid: 2 issues for the Overseer sub-tab.');
      expect(all('.fi-notices li').map(text)).toEqual(['issues[1] (id 7): this item changes nothing.']);
    });

    it('lists the server\'s errors when the preview refuses the file', async () => {
      previewResolutions.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'The file has 1 error.', errors: [{ index: 0, message: 'issues[0] (id 42): no Overseer feedback issue has id 42.' }] }
      })));
      await toReview();

      expect(component.step).toBe(1);
      expect(all('.fi-errors li').map(text)).toEqual(['issues[0] (id 42): no Overseer feedback issue has id 42.']);
    });

    it('previews the file and shows one card per item, the subject as plain text', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem(), unchangedSeven()])));
      await toReview();

      expect(previewResolutions).toHaveBeenCalledWith({
        format: 'overseer-feedback-resolutions', version: 1, source: 'overseer', exportedAtUtc: '2026-10-23T12:34:56Z',
        items: [{ id: 42, fingerprint: FP, status: 'resolved', resolution: 'Fixed the wiki search.' }, { id: 7, fingerprint: FP2 }]
      });
      expect(component.step).toBe(2);
      expect(text(q('.fi-outcome p'))).toBe('This file changes 1 of 2 issues in the Overseer sub-tab.');

      const cards = all('.fi-card');
      expect(cards.length).toBe(2);
      expect(text(cards[0].querySelector('h5'))).toBe('Issue #42');
      expect(Array.from(cards[0].querySelectorAll('.status-badge')).map(text)).toEqual(['status New → Resolved', 'resolution']);
      expect(text(cards[0].querySelector('.fi-subject'))).toBe('wiki_search finds <b>nothing</b>');
      expect(cards[0].querySelector('.fi-subject b')).toBeNull();
      expect(text(cards[0].querySelector('.fi-status-line'))).toBe('New → Resolved');
      expect(Array.from(cards[1].querySelectorAll('.status-badge')).map(text)).toEqual(['no changes']);
      expect(text(cards[1].querySelector('.fi-status-line'))).toBe('Acknowledged (unchanged)');
      expect(footerButton('Apply 1 change').getAttribute('aria-disabled')).toBeNull();
    });

    it('switches the text fields between side by side and diff', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem()])));
      await toReview();

      const card = all('.fi-card')[0];
      expect(card.querySelector('app-text-diff .text-diff-side')).not.toBeNull();
      (all('.fi-view-toggle button').find(b => text(b) === 'Diff') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(card.querySelector('app-text-diff .text-diff-side')).toBeNull();
      expect(Array.from(card.querySelectorAll('app-text-diff .diff-line')).map(text)).toEqual(['+ Fixed the wiki search.']);
    });

    it('applies with the status and triage time the preview showed, then reports the counts', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem(), unchangedSeven()])));
      applyResolutions.mockReturnValue(of({ changed: 1, unchanged: 1 }));
      await toReview();

      footerButton('Apply 1 change').click();
      fixture.detectChanges();

      const body = applyResolutions.mock.lastCall![0];
      expect(body.items).toEqual([
        { id: 42, fingerprint: FP, status: 'resolved', resolution: 'Fixed the wiki search.', expectedStatus: 'new', expectedTriagedUtc: null },
        { id: 7, fingerprint: FP2, expectedStatus: 'acknowledged', expectedTriagedUtc: '2026-10-20T09:00:00Z' }
      ]);
      expect(component.step).toBe(3);
      expect(text(q('.fi-done'))).toBe('Changed 1 issue; 1 unchanged.');
      expect(imported).toHaveBeenCalledWith({ changed: 1, unchanged: 1 });
      expect(footerButton('Close')).toBeTruthy();
    });

    it('keeps Apply unavailable until the issues triaged since the export are acknowledged', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem({ triagedSinceExport: true }), unchangedSeven()])));
      applyResolutions.mockReturnValue(of({ changed: 1, unchanged: 1 }));
      await toReview();

      expect(q('.fi-triaged-warning')).not.toBeNull();
      expect(Array.from(all('.fi-card')[0].querySelectorAll('.status-badge')).map(text)).toContain('Triaged since export');
      const apply = footerButton('Apply 1 change');
      expect(apply.getAttribute('aria-disabled')).toBe('true');
      expect(text(q(`#${apply.getAttribute('aria-describedby')}`))).toBe('Confirm that you have reviewed the issues changed since the export.');
      apply.click();
      expect(applyResolutions).not.toHaveBeenCalled();

      const checkbox = q<HTMLInputElement>('.fi-confirm input[type="checkbox"]')!;
      expect(text(q('.fi-confirm label'))).toBe('I have reviewed the issues changed since the export');
      checkbox.click();
      fixture.detectChanges();
      expect(footerButton('Apply 1 change').getAttribute('aria-disabled')).toBeNull();
      footerButton('Apply 1 change').click();
      expect(applyResolutions).toHaveBeenCalled();
    });

    it('shows new occurrences and a regression as notices that do not block', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem({ occurrencesSinceExport: 1, regressedSinceExport: true })])));
      await toReview();

      const badges = Array.from(all('.fi-card')[0].querySelectorAll('.status-badge')).map(text);
      expect(badges).toContain('1 new occurrence since export');
      expect(badges).toContain('Regressed since export');
      expect(footerButton('Apply 1 change').getAttribute('aria-disabled')).toBeNull();
    });

    it('refuses to apply a file with an item that cannot be applied', async () => {
      previewResolutions.mockReturnValue(of(preview([
        previewItem({ exists: false, current: null, after: null, hasChanges: false, errors: ['issues[0] (id 42): no Overseer feedback issue has id 42.'] }),
        unchangedSeven()
      ])));
      await toReview();

      expect(text(all('.fi-card')[0].querySelector('.fi-item-errors'))).toContain('no Overseer feedback issue has id 42');
      const apply = all('.dialog-footer .fi-apply-btn')[0];
      expect(apply.getAttribute('aria-disabled')).toBe('true');
      expect(text(q(`#${apply.getAttribute('aria-describedby')}`))).toBe('One issue cannot be applied. Correct the file and import it again.');
    });

    it('shows a conflict inline and reviews the file again', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem(), unchangedSeven()])));
      applyResolutions.mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 409,
        error: {
          message: 'One issue changed after the preview, so nothing was applied. Review the file again.',
          conflicts: [{ index: 0, id: 42, fingerprint: FP, reason: 'changed', expectedStatus: 'new', currentStatus: 'dismissed', expectedTriagedUtc: null, currentTriagedUtc: '2026-10-23T13:00:00Z' }]
        }
      })));
      await toReview();

      footerButton('Apply 1 change').click();
      fixture.detectChanges();

      expect(component.step).toBe(2);
      expect(text(q('.fi-conflicts-message'))).toBe('One issue changed after the preview, so nothing was applied. Review the file again.');
      expect(all('.fi-conflicts li').map(text)).toEqual(['Issue #42: the status was New at the review and is Dismissed now.']);
      expect(footerButton('Apply 1 change').getAttribute('aria-disabled')).toBe('true');
      expect(imported).not.toHaveBeenCalled();

      const dismissed = previewItem({
        current: { status: 'dismissed', note: null, resolution: null, fixedInVersion: null, triagedUtc: '2026-10-23T13:00:00Z', lastSeenUtc: null },
        triagedSinceExport: true,
        changes: { status: true, note: false, resolution: true, fixedInVersion: false }
      });
      previewResolutions.mockReturnValue(of(preview([dismissed, unchangedSeven()])));
      q<HTMLButtonElement>('.fi-review-again')!.click();
      fixture.detectChanges();

      expect(previewResolutions).toHaveBeenCalledTimes(2);
      expect(q('.fi-conflicts')).toBeNull();
      expect(text(all('.fi-card')[0].querySelector('.fi-status-line'))).toBe('Dismissed → Resolved');
      expect(q('.fi-triaged-warning')).not.toBeNull();
    });

    it('goes back to the text, which can be reviewed again', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem(), unchangedSeven()])));
      await toReview();

      footerButton('Back').click();
      fixture.detectChanges();
      expect(component.step).toBe(1);
      expect(q<HTMLTextAreaElement>('textarea')).not.toBeNull();
    });

    it('starts afresh when opened again', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem(), unchangedSeven()])));
      await toReview();
      component.close();
      component.open();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(component.step).toBe(1);
      expect(component.pastedText).toBe('');
      expect(q('.fi-card')).toBeNull();
    });

    it('has no duplicate element ids', async () => {
      previewResolutions.mockReturnValue(of(preview([previewItem({ triagedSinceExport: true }), unchangedSeven()])));
      await toReview();
      const ids = all('[id]').map(e => e.id);
      expect(new Set(ids).size).toBe(ids.length);
    });
  });
});
