import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { KeyFigureCardActionsComponent, KeyFigureCardExportRequest } from './key-figure-card-actions.component';

@Component({
  standalone: true,
  imports: [KeyFigureCardActionsComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="score-card" style="position: relative;">
      <span class="score-label">Reference Reader Agreement</span>
      <span class="score-subvalue">2.1 pts</span>
      <app-key-figure-card-actions [runId]="72" cardLabel="Reference Reader Agreement" [busy]="busy"
                                   (exportRequested)="requests.push($event)" />
    </div>
  `
})
class CardHostComponent {
  busy = false;
  requests: KeyFigureCardExportRequest[] = [];
}

@Component({
  standalone: true,
  imports: [KeyFigureCardActionsComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="score-card" style="position: relative;">
      <span class="score-label">Total Cost</span>
      <span class="score-subvalue">$3.21</span>
      <app-key-figure-card-actions [runId]="7" cardLabel="Total Cost" subjectLabel="battery run 7" subjectKey="battery7" />
    </div>
    <div class="score-card" style="position: relative;">
      <span class="score-label">Total Cost</span>
      <span class="score-subvalue">$1.08</span>
      <app-key-figure-card-actions [runId]="7" cardLabel="Total Cost" />
    </div>
    <div class="score-card" style="position: relative;">
      <span class="score-label">Total Cost</span>
      <span class="score-subvalue">$2.00</span>
      <app-key-figure-card-actions [runId]="8" cardLabel="Total Cost" subjectLabel="battery run 8" subjectKey="battery8" formatLabel="WebP" />
    </div>
  `
})
class SubjectHostComponent {}

describe('KeyFigureCardActionsComponent', () => {
  let fixture: ComponentFixture<CardHostComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CardHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(CardHostComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  const buttons = (): HTMLButtonElement[] =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('app-key-figure-card-actions button'));

  it('renders two icon-only buttons named for the card and the run', () => {
    const [copy, download] = buttons();
    expect(buttons().length).toBe(2);
    expect(copy.getAttribute('aria-label')).toBe('Copy Reference Reader Agreement of run 72 as an image');
    expect(download.getAttribute('aria-label')).toBe('Download Reference Reader Agreement of run 72 as a PNG image');
    for (const button of buttons()) {
      expect(button.getAttribute('type')).toBe('button');
      expect(button.classList.contains('action-btn')).toBe(true);
      expect(button.hasAttribute('title')).toBe(false);
      expect(button.hasAttribute('disabled')).toBe(false);
      expect(button.textContent?.trim()).toBe('');
      expect(button.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    }
  });

  it('pairs each button with a hint tooltip anchored on both ends by name', () => {
    const expected = [
      ['kfc-copy-run72-reference-reader-agreement', 'Copy as image'],
      ['kfc-download-run72-reference-reader-agreement', 'Download as PNG']
    ];
    buttons().forEach((button, index) => {
      const [id, text] = expected[index];
      expect(button.getAttribute('interestfor')).toBe(id);
      expect(button.getAttribute('style')).toContain(`anchor-name: --${id}`);
      const tip = host.querySelector(`#${id}`) as HTMLElement;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.classList.contains('gh-tooltip')).toBe(true);
      expect(tip.getAttribute('style')).toContain(`position-anchor: --${id}`);
      expect(tip.textContent?.trim()).toBe(text);
    });
  });

  it('emits both actions with the enclosing score card', () => {
    const card = host.querySelector('.score-card') as HTMLElement;
    const [copy, download] = buttons();

    copy.click();
    download.click();

    expect(fixture.componentInstance.requests).toEqual([
      { action: 'copy', card },
      { action: 'download', card }
    ]);
  });

  it('marks both buttons aria-disabled while busy and refuses the click', () => {
    fixture.componentInstance.busy = true;
    fixture.detectChanges();

    for (const button of buttons()) {
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.disabled).toBe(false);
      button.click();
    }
    expect(fixture.componentInstance.requests).toEqual([]);

    fixture.componentInstance.busy = false;
    fixture.detectChanges();
    expect(buttons().every(button => !button.hasAttribute('aria-disabled'))).toBe(true);
  });

  it('is hidden by opacity only, so both buttons stay in the tab order', () => {
    const actions = host.querySelector('app-key-figure-card-actions') as HTMLElement;
    const style = getComputedStyle(actions);
    expect(style.position).toBe('absolute');
    expect(style.display).toBe('flex');
    expect(style.visibility).toBe('visible');
    expect(buttons().every(button => button.tabIndex === 0)).toBe(true);
  });
});

describe('KeyFigureCardActionsComponent with a host subject', () => {
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SubjectHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(SubjectHostComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  const buttonsOf = (index: number): HTMLButtonElement[] =>
    Array.from(host.querySelectorAll('app-key-figure-card-actions')[index].querySelectorAll<HTMLButtonElement>('button'));

  it('names both buttons for the subject the host gives', () => {
    const [copy, download] = buttonsOf(0);
    expect(copy.getAttribute('aria-label')).toBe('Copy Total Cost of battery run 7 as an image');
    expect(download.getAttribute('aria-label')).toBe('Download Total Cost of battery run 7 as a PNG image');
  });

  it('names the download by the format the host gives, PNG by default', () => {
    const [, pngDownload] = buttonsOf(1);
    expect(pngDownload.getAttribute('aria-label')).toBe('Download Total Cost of run 7 as a PNG image');
    const [, webpDownload] = buttonsOf(2);
    expect(webpDownload.getAttribute('aria-label')).toBe('Download Total Cost of battery run 8 as a WebP image');
    expect(host.querySelector('#kfc-download-battery8-total-cost')?.textContent?.trim()).toBe('Download as WebP');
    expect(buttonsOf(2)[0].getAttribute('aria-label')).toBe('Copy Total Cost of battery run 8 as an image');
  });

  it('keys its tooltips by the subject, apart from a run with the same id', () => {
    const ids = [
      ['kfc-copy-battery7-total-cost', 'kfc-download-battery7-total-cost'],
      ['kfc-copy-run7-total-cost', 'kfc-download-run7-total-cost']
    ];
    ids.forEach((pair, index) => {
      buttonsOf(index).forEach((button, action) => {
        const id = pair[action];
        expect(button.getAttribute('interestfor')).toBe(id);
        expect(button.getAttribute('style')).toContain(`anchor-name: --${id}`);
        expect(host.querySelectorAll(`#${id}`).length).toBe(1);
        expect(host.querySelector(`#${id}`)?.getAttribute('style')).toContain(`position-anchor: --${id}`);
      });
    });
  });
});
