import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import type { CorpusIndexStatus } from '../../../../services/admin-devtools.service';
import { buildCorpusIndexInfo, buildCorpusJob, buildCorpusStatus } from '../../../admin.component.testing';
import { CorpusIndexPanelComponent } from './corpus-index-panel.component';

const nf = new Intl.NumberFormat();

@Component({
  standalone: true,
  imports: [CorpusIndexPanelComponent],
  template: `
    <app-corpus-index-panel [status]="status" corpusLabel="GnollHack wiki" idPrefix="gw" reindexLabel="Re-index wiki">
      <div figures class="gh-health-tile extra-tile"><dt>Categories</dt><dd>12</dd></div>
    </app-corpus-index-panel>`
})
class ProjectionHostComponent {
  status: CorpusIndexStatus = buildCorpusStatus('gnollhack-wiki', { index: buildCorpusIndexInfo() });
}

describe('CorpusIndexPanelComponent', () => {
  let fixture: ComponentFixture<CorpusIndexPanelComponent>;
  let component: CorpusIndexPanelComponent;
  let el: HTMLElement;
  let reindexCount: number;
  let retryCount: number;

  const q = <T extends Element = HTMLElement>(selector: string) => el.querySelector<T>(selector);
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const reindexButton = () => q<HTMLButtonElement>('#gw-reindex-btn')!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CorpusIndexPanelComponent, ProjectionHostComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(CorpusIndexPanelComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    reindexCount = 0;
    retryCount = 0;
    component.reindex.subscribe(() => reindexCount++);
    component.retry.subscribe(() => retryCount++);
    fixture.componentRef.setInput('corpusLabel', 'GnollHack wiki');
    fixture.componentRef.setInput('idPrefix', 'gw');
    fixture.componentRef.setInput('reindexLabel', 'Re-index wiki');
    fixture.componentRef.setInput('reindexTip', 'Reads the wiki folder as it is now.');
  });

  function render(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  it('shows a loading line until the first status arrives, and hides the key figures', () => {
    render({ status: null });
    expect(text(q('.cip-loading'))).toBe('Loading the GnollHack wiki status…');
    expect(q('.cip-loading')!.getAttribute('role')).toBe('status');
    expect(q<HTMLElement>('.cip-figures-card')!.hidden).toBe(true);
    expect(q('.gh-dash-banner')).toBeNull();
  });

  it('shows a failed poll with a Retry that emits retry', () => {
    render({ status: null, loadError: 'The status could not be loaded. Trying again in 5 s.' });
    expect(q('.cip-loading')).toBeNull();
    const error = q('#gw-load-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(text(error)).toBe('The status could not be loaded. Trying again in 5 s.');
    const retry = q<HTMLButtonElement>('.gh-dash-load-error button')!;
    expect(retry.type).toBe('button');
    expect(retry.getAttribute('aria-describedby')).toBe('gw-load-error');
    retry.click();
    expect(retryCount).toBe(1);
  });

  it('renders the banner as a status region with the corpus banner word and sentence', () => {
    render({ status: buildCorpusStatus('gnollhack-wiki', { index: buildCorpusIndexInfo() }) });
    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('role')).toBe('status');
    expect(banner.getAttribute('data-status')).toBe('ok');
    expect(banner.querySelector('.gh-health-status')!.getAttribute('data-status')).toBe('ok');
    expect(text(banner.querySelector('app-health-status'))).toBe('Ready');
    expect(text(banner.querySelector('.gh-dash-banner-sentence'))).toBe('The index matches the current HEAD.');
  });

  it('emits reindex from the action button while the action is available', () => {
    render({ status: buildCorpusStatus('gnollhack-wiki', { index: buildCorpusIndexInfo() }) });
    const button = reindexButton();
    expect(button.classList).toContain('btn-gh');
    expect(button.type).toBe('button');
    expect(text(button)).toBe('Re-index wiki');
    expect(button.querySelector('svg.btn-icon')!.getAttribute('aria-hidden')).toBe('true');
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    expect(button.getAttribute('aria-describedby')).toBe('gw-reindex-tip');
    expect(q('app-info-tip')).not.toBeNull();
    button.click();
    expect(reindexCount).toBe(1);
  });

  it('marks the action aria-disabled with a visible reason while a pass runs, and does not emit', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki', {
        currentJob: buildCorpusJob({ outcome: 'Running', finishedAtUtc: null, phase: 'BuildingIndex' })
      })
    });
    const button = reindexButton();
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-describedby')).toBe('gw-actions-reason gw-reindex-tip');
    const reason = q('#gw-actions-reason')!;
    expect(reason.classList).toContain('gh-field-warning');
    expect(text(reason)).toBe('A pass is running.');
    button.click();
    expect(reindexCount).toBe(0);
  });

  it('blocks the action while a start request is in flight', () => {
    render({ status: buildCorpusStatus('gnollhack-wiki'), starting: true });
    expect(reindexButton().getAttribute('aria-disabled')).toBe('true');
    expect(text(q('#gw-actions-reason'))).toBe('A pass is running.');
  });

  it('names the key when the corpus is not configured or its folder is missing', () => {
    render({ status: buildCorpusStatus('knowledge-base', { configured: false, path: null, pathExists: false }) });
    expect(text(q('#gw-actions-reason'))).toBe('KbPath is not configured.');

    render({ status: buildCorpusStatus('nethack-wiki', { pathExists: false }) });
    expect(text(q('#gw-actions-reason'))).toBe('The NetHackWikiPath folder does not exist.');
  });

  it('allows the action on a built-in default folder that exists', () => {
    render({ status: buildCorpusStatus('gnollhack-wiki', { configured: false, path: 'c:\\wiki' }) });
    expect(reindexButton().hasAttribute('aria-disabled')).toBe(false);
  });

  it('shows a 409 refusal as a warning and any other as an error, in a status region the button points at', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki'),
      actionMessage: 'A pass over the GnollHack wiki is already running.',
      actionMessageKind: 'warning'
    });
    const feedback = q('.cip-action-feedback')!;
    expect(feedback.getAttribute('role')).toBe('status');
    expect(q('#gw-action-message')!.classList).toContain('gh-field-warning');
    expect(text(q('#gw-action-message'))).toBe('A pass over the GnollHack wiki is already running.');
    expect(reindexButton().getAttribute('aria-describedby')).toBe('gw-action-message gw-reindex-tip');

    render({ actionMessage: 'The WikiPath folder does not exist.', actionMessageKind: 'error' });
    expect(q('#gw-action-message')!.classList).toContain('gh-field-error');
  });

  it('draws the progress card of a running pass with its stages and a determinate bar', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki', {
        currentJob: buildCorpusJob({ outcome: 'Running', finishedAtUtc: null, phase: 'ReadingFiles', filesRead: 1204, filesTotal: 2512 })
      })
    });
    expect(text(q('#gw-progress-title'))).toBe('GnollHack wiki pass in progress');
    const stages = Array.from(el.querySelectorAll('.cip-rail .run-stage'));
    expect(stages.map(s => text(s.querySelector('.run-stage-name')))).toEqual(['Reading files', 'Building index', 'Publishing index']);
    expect(stages[0].getAttribute('aria-current')).toBe('step');
    expect(stages[0].classList).toContain('is-current');
    expect(stages[1].classList).not.toContain('is-done');
    const bar = q<HTMLProgressElement>('.cip-progress-bar')!;
    expect(bar.getAttribute('value')).toBe('1204');
    expect(bar.getAttribute('max')).toBe('2512');
    expect(bar.getAttribute('aria-label')).toBe('GnollHack wiki pass progress');
    expect(text(q('.cip-progress-text'))).toBe(`${nf.format(1204)} of ${nf.format(2512)} files`);
    expect(text(q('.cip-job-meta'))).toContain('Started manually');
    expect(q('.cip-job-meta time')!.getAttribute('datetime')).toMatch(/^PT\d+\.\dS$/);
  });

  it('uses an indeterminate bar while the file total is unknown', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki', {
        currentJob: buildCorpusJob({ outcome: 'Running', finishedAtUtc: null, phase: 'BuildingIndex' })
      })
    });
    expect(q('.cip-progress-bar')!.hasAttribute('value')).toBe(false);
    expect(q('.cip-progress-text')).toBeNull();
  });

  it('shows the host\'s ended job with its outcome and where it stopped', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki'),
      endedJob: buildCorpusJob({ outcome: 'Failed', phase: 'BuildingIndex', message: 'Lucene failed.' })
    });
    expect(text(q('#gw-progress-title'))).toBe('GnollHack wiki pass finished');
    expect(text(q('.cip-progress .gh-dash-card-head app-health-status'))).toBe('Failed');
    const stages = Array.from(el.querySelectorAll('.cip-rail .run-stage'));
    expect(stages[0].classList).toContain('is-done');
    expect(stages[1].classList).toContain('is-ended');
    expect(text(stages[1].querySelector('.run-stage-note'))).toBe('Failed here');
    expect(q('.cip-progress-bar')).toBeNull();
    expect(text(q('.cip-job-message'))).toBe('Lucene failed.');
  });

  it('has no progress card when nothing runs or lingers', () => {
    render({ status: buildCorpusStatus('gnollhack-wiki') });
    expect(q('.cip-progress')).toBeNull();
  });

  it('shows the key figures of a Git-polled index, with the current HEAD when it moved', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki', {
        index: buildCorpusIndexInfo({ fileCount: 2512, headSha: 'aaaaaaaaaaaaaaaa', branch: 'main' }),
        currentHeadSha: 'bbbbbbbbbbbbbbbb',
        currentBranch: 'main',
        nextCheckAtUtc: '2026-10-18T12:10:00Z'
      })
    });
    const tiles = Array.from(el.querySelectorAll('.cip-figures > .gh-health-tile'));
    expect(tiles.map(t => text(t.querySelector('dt')))).toEqual([
      'Articles', 'Total size', 'Last indexed', 'Index time', 'Indexed commit', 'Content fingerprint', 'Next automatic check'
    ]);
    expect(text(tiles[0].querySelector('dd'))).toBe(nf.format(2512));
    expect(text(tiles[4].querySelector('code'))).toBe('aaaaaaaaaa');
    expect(text(tiles[4].querySelector('.cip-current-head'))).toBe('Current HEAD bbbbbbbbbb on main');
    expect(text(tiles[6].querySelector('.gh-health-tile-note'))).toBe('Every 10 min');

    const timeButton = q<HTMLButtonElement>('#gw-last-indexed-btn')!;
    expect(timeButton.getAttribute('interestfor')).toBe('gw-last-indexed-tip');
    expect(q('#gw-last-indexed-tip')!.getAttribute('popover')).toBe('hint');
    expect(text(q('.cip-path'))).toBe('Folder: C:\\wiki');
  });

  it('shows Refresh instead of the commit and the next check for the NetHack wiki', () => {
    render({ status: buildCorpusStatus('nethack-wiki', { index: buildCorpusIndexInfo() }) });
    const labels = Array.from(el.querySelectorAll('.cip-figures > .gh-health-tile dt')).map(dt => text(dt));
    expect(labels).toContain('Refresh');
    expect(labels).not.toContain('Indexed commit');
    expect(labels).not.toContain('Next automatic check');
    const refresh = Array.from(el.querySelectorAll('.cip-figures > .gh-health-tile')).find(t => text(t.querySelector('dt')) === 'Refresh')!;
    expect(text(refresh.querySelector('dd'))).toBe('Startup and manual only');
  });

  it('says no index has been published when there is none', () => {
    render({ status: buildCorpusStatus('gnollhack-wiki') });
    expect(text(q('.cip-figures-card .gh-dash-muted'))).toBe('No index has been published yet.');
    expect(el.querySelectorAll('.cip-figures > .gh-health-tile').length).toBe(0);
  });

  it('copies the full content fingerprint and says so', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    try {
      render({ status: buildCorpusStatus('gnollhack-wiki', { index: buildCorpusIndexInfo({ fingerprintShort: 'abcdef012345' }) }) });
      const button = q<HTMLButtonElement>('#gw-copy-fingerprint-btn')!;
      expect(button.classList).toContain('action-btn');
      expect(button.getAttribute('aria-label')).toBe('Copy content fingerprint');
      expect(button.getAttribute('interestfor')).toBe('gw-copy-fingerprint-tip');
      expect(button.hasAttribute('title')).toBe(false);
      button.click();
      await new Promise(resolve => setTimeout(resolve, 0));
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith('abcdef012345');
      expect(text(q('.cip-copy-status'))).toBe('Content fingerprint copied.');
      expect(q('.cip-copy-status')!.getAttribute('role')).toBe('status');
    } finally {
      delete (navigator as { clipboard?: unknown }).clipboard;
      if (original) Object.defineProperty(navigator, 'clipboard', original);
    }
  });

  it('lists the excluded and unreadable files, with the size key and a count of those not listed', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki', {
        index: buildCorpusIndexInfo({
          maxFileSizeKB: 100,
          skippedBySize: [{ path: 'Big/Page.md', bytes: 204800 }],
          skippedBySizeCount: 3,
          skippedDotDirectories: 4,
          failedFiles: [{ path: 'Locked.md', error: 'IOException: The file is in use.' }],
          failedFileCount: 1
        })
      })
    });
    const size = q('.cip-excluded-size')!;
    expect(text(size.querySelector('summary'))).toBe('Excluded by size (3)');
    expect(text(size.querySelector('.gh-dash-note'))).toBe('Files over 100 KB (MaxWikiFileSizeKB) are not indexed.');
    expect(text(size.querySelector('li code'))).toBe('Big/Page.md');
    expect(text(size.querySelector('.cip-more'))).toBe('and 2 more');

    expect(text(q('.cip-excluded-dot summary'))).toBe('In dot-directories (4)');

    const unreadable = q('.cip-unreadable')!;
    expect(text(unreadable.querySelector('summary'))).toBe('Unreadable (1)');
    const codes = Array.from(unreadable.querySelectorAll('li code')).map(c => text(c));
    expect(codes).toEqual(['Locked.md', 'IOException: The file is in use.']);
    expect(unreadable.querySelector('.cip-more')).toBeNull();
  });

  it('says the knowledge base has no size limit', () => {
    render({ status: buildCorpusStatus('knowledge-base', { index: buildCorpusIndexInfo({ maxFileSizeKB: null }) }) });
    expect(text(q('.cip-excluded-size .gh-dash-note'))).toBe('This corpus has no size limit.');
  });

  it('lists recent passes newest first in a labeled, scrollable table', () => {
    render({
      status: buildCorpusStatus('gnollhack-wiki', {
        recentJobs: [
          buildCorpusJob({ id: 'old', trigger: 'Startup', finishedAtUtc: '2026-10-18T09:00:05Z', startedAtUtc: '2026-10-18T09:00:00Z', message: 'first' }),
          buildCorpusJob({ id: 'new', trigger: 'RepositoryChange', outcome: 'Failed', finishedAtUtc: '2026-10-18T11:00:05Z', startedAtUtc: '2026-10-18T11:00:00Z', message: 'second' })
        ]
      })
    });
    const wrap = q('.gh-dash-table-wrap')!;
    expect(wrap.getAttribute('role')).toBe('region');
    expect(wrap.getAttribute('tabindex')).toBe('0');
    expect(wrap.getAttribute('aria-labelledby')).toBe('gw-recent-title');
    const rows = Array.from(el.querySelectorAll('.cip-recent tbody tr'));
    expect(rows.map(r => text(r.querySelector('.cip-message-cell')))).toEqual(['second', 'first']);
    expect(text(rows[0].querySelector('td[data-label="Trigger"]'))).toBe('Repository change');
    expect(text(rows[0].querySelector('td[data-label="Outcome"]'))).toBe('Failed');
    expect(text(rows[1].querySelector('td[data-label="Duration"]'))).toBe('5.0 s');
  });

  it('says when no pass has finished', () => {
    render({ status: buildCorpusStatus('gnollhack-wiki') });
    expect(Array.from(el.querySelectorAll('.gh-dash-muted')).map(p => text(p)))
      .toContain('No pass has finished since the server started.');
  });

  it('projects the host\'s figures at the end of the key-figures list', () => {
    const host = TestBed.createComponent(ProjectionHostComponent);
    host.detectChanges();
    const tiles = Array.from((host.nativeElement as HTMLElement).querySelectorAll('.cip-figures > .gh-health-tile'));
    expect(tiles.length).toBeGreaterThan(1);
    expect(tiles[tiles.length - 1].classList).toContain('extra-tile');
    expect(text(tiles[tiles.length - 1].querySelector('dt'))).toBe('Categories');
  });
});
