import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RunReportFrameComponent } from './run-report-frame.component';

@Component({
  standalone: true,
  imports: [RunReportFrameComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="shell" [style.width.px]="width" style="height: 640px; display: flex; flex-direction: column;">
      <app-run-report-frame mainLabel="Findings and questions" asideLabel="Run details" [busy]="busy">
        <h3 runReportHeader class="t-title">Run #7</h3>
        <button runReportActions type="button" class="t-action">Downloads</button>
        <div runReportFigures class="t-figure">Quality 80</div>
        <div runReportFigures class="t-figure">Cost $4</div>
        <div runReportFigures class="t-figure">Speed 12 s</div>
        <p runReportMain class="t-main">Main content</p>
        <p runReportAside class="t-aside">Aside content</p>
      </app-run-report-frame>
    </div>
  `
})
class FrameHostComponent {
  width = 1200;
  busy = false;
}

describe('RunReportFrameComponent', () => {
  let fixture: ComponentFixture<FrameHostComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [FrameHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(FrameHostComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  const q = (selector: string): HTMLElement => host.querySelector<HTMLElement>(selector)!;
  const columnCount = (): number =>
    getComputedStyle(q('.rrf-columns')).gridTemplateColumns.split(' ').filter(part => part !== '').length;

  function setWidth(width: number): void {
    fixture.componentInstance.width = width;
    fixture.detectChanges();
  }

  it('projects every slot into its place', () => {
    expect(q('.rrf-header .rrf-identity .t-title').textContent).toBe('Run #7');
    expect(q('.rrf-header .rrf-actions .t-action')).not.toBeNull();
    expect(host.querySelectorAll('.rrf-figures > .t-figure').length).toBe(3);
    expect(q('section.rrf-main .t-main').textContent).toBe('Main content');
    expect(q('section.rrf-aside .t-aside').textContent).toBe('Aside content');
  });

  it('names both sections and makes them focusable', () => {
    const main = q('section.rrf-main');
    const aside = q('section.rrf-aside');

    expect(main.getAttribute('aria-label')).toBe('Findings and questions');
    expect(aside.getAttribute('aria-label')).toBe('Run details');
    expect(main.getAttribute('tabindex')).toBe('0');
    expect(aside.getAttribute('tabindex')).toBe('0');
  });

  it('marks the body busy only while busy', () => {
    expect(q('.rrf-body').hasAttribute('aria-busy')).toBeFalse();

    fixture.componentInstance.busy = true;
    fixture.detectChanges();

    expect(q('.rrf-body').getAttribute('aria-busy')).toBe('true');
  });

  it('makes the body an inline-size container named run-report', () => {
    const style = getComputedStyle(q('.rrf-body'));

    expect(style.containerName).toBe('run-report');
    expect(style.containerType).toBe('inline-size');
  });

  it('shows two independently scrolling columns from 60rem and one below', () => {
    setWidth(1200);
    expect(columnCount()).toBe(2);
    expect(getComputedStyle(q('.rrf-main')).overflowY).toBe('auto');
    expect(getComputedStyle(q('.rrf-aside')).overflowY).toBe('auto');
    expect(getComputedStyle(q('.rrf-aside')).overscrollBehaviorY).toBe('contain');

    setWidth(700);
    expect(columnCount()).toBe(1);
    expect(getComputedStyle(q('.rrf-main')).overflowY).toBe('visible');
  });

  it('lays the key figures out as an auto-fit grid', () => {
    setWidth(1200);
    const figures = getComputedStyle(q('.rrf-figures'));

    expect(figures.display).toBe('grid');
    expect(figures.gridTemplateColumns.split(' ').length).toBeGreaterThan(1);
  });

  it('keeps the header sticky', () => {
    setWidth(700);
    expect(getComputedStyle(q('.rrf-header')).position).toBe('sticky');

    setWidth(1200);
    expect(getComputedStyle(q('.rrf-top')).position).toBe('sticky');
  });

  it('has no key-figures bar unless the figures are collapsible', () => {
    expect(host.querySelector('.rrf-figures-bar')).toBeNull();
    expect(host.querySelector('.rrf-figures-toggle')).toBeNull();
    expect(q('.rrf-figures').hasAttribute('id')).toBeFalse();
    expect(q('.rrf-figures').hasAttribute('hidden')).toBeFalse();
  });
});

const STORAGE_KEY = 'test.runReportFrame.figuresCollapsed';

@Component({
  standalone: true,
  imports: [RunReportFrameComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <app-run-report-frame [figuresCollapsible]="true" figuresLabel="Key figures" [figuresStorageKey]="storageKey">
      <h3 runReportHeader>Run #7</h3>
      <div runReportFigures class="t-figure">Quality 80</div>
      <div runReportFigures class="t-figure">Cost $4</div>
      <span runReportFiguresSummary class="t-summary">Quality 80 · Cost $4</span>
      <div runReportFiguresActions class="t-figure-actions"><button type="button">Copy</button></div>
      <p runReportMain>Main</p>
      <p runReportAside>Aside</p>
    </app-run-report-frame>
  `
})
class CollapsibleHostComponent {
  storageKey: string | null = STORAGE_KEY;
}

describe('RunReportFrameComponent with collapsible figures', () => {
  let fixture: ComponentFixture<CollapsibleHostComponent>;
  let host: HTMLElement;

  const q = (selector: string): HTMLElement => host.querySelector<HTMLElement>(selector)!;
  const toggle = (): HTMLButtonElement => q('.rrf-figures-toggle') as HTMLButtonElement;

  function create(): void {
    fixture = TestBed.createComponent(CollapsibleHostComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    localStorage.removeItem(STORAGE_KEY);
    await TestBed.configureTestingModule({ imports: [CollapsibleHostComponent] }).compileComponents();
  });

  afterEach(() => {
    localStorage.removeItem(STORAGE_KEY);
  });

  it('puts a disclosure toggle, the summary and the actions in a bar above the strip, expanded by default', () => {
    create();
    const bar = q('.rrf-top .rrf-figures-bar');
    const figures = q('.rrf-figures');

    expect(bar).not.toBeNull();
    expect(bar.nextElementSibling).toBe(figures);
    expect(toggle().getAttribute('type')).toBe('button');
    expect(toggle().textContent?.trim()).toBe('Key figures');
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(toggle().getAttribute('aria-controls')).toBe(figures.id);
    expect(figures.id).toMatch(/^rrf-figures-\d+$/);
    expect(figures.hidden).toBeFalse();
    expect(q('.rrf-figures-actions .t-figure-actions')).not.toBeNull();
    expect(host.querySelectorAll('.rrf-figures > .t-figure').length).toBe(2);
    expect(host.querySelector('details, summary')).toBeNull();
  });

  it('flips aria-expanded and hides the strip without removing its cards', () => {
    create();
    toggle().click();
    fixture.detectChanges();

    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(q('.rrf-figures').hidden).toBeTrue();
    expect(getComputedStyle(q('.rrf-figures')).display).toBe('none');
    expect(host.querySelectorAll('.rrf-figures > .t-figure').length).toBe(2);

    toggle().click();
    fixture.detectChanges();
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(q('.rrf-figures').hidden).toBeFalse();
  });

  it('shows the summary only while collapsed', () => {
    create();
    const summary = q('.rrf-figures-summary');
    expect(summary.querySelector('.t-summary')).not.toBeNull();
    expect(summary.hidden).toBeTrue();
    expect(getComputedStyle(summary).display).toBe('none');

    toggle().click();
    fixture.detectChanges();
    expect(summary.hidden).toBeFalse();
    expect(getComputedStyle(summary).display).not.toBe('none');
    expect(summary.textContent?.trim()).toBe('Quality 80 · Cost $4');
  });

  it('gives every frame its own figures id', () => {
    create();
    const first = q('.rrf-figures').id;
    create();
    expect(q('.rrf-figures').id).not.toBe(first);
  });

  it('remembers the collapsed state and restores it', () => {
    create();
    toggle().click();
    fixture.detectChanges();
    expect(localStorage.getItem(STORAGE_KEY)).toBe('1');

    create();
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(q('.rrf-figures').hidden).toBeTrue();

    toggle().click();
    fixture.detectChanges();
    expect(localStorage.getItem(STORAGE_KEY)).toBe('0');
  });

  it('stays expanded, and still toggles, when storage throws', () => {
    localStorage.setItem(STORAGE_KEY, '1');
    const getItem = spyOn(Storage.prototype, 'getItem').and.throwError('denied');
    const setItem = spyOn(Storage.prototype, 'setItem').and.throwError('denied');

    create();
    expect(getItem).toHaveBeenCalled();
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(q('.rrf-figures').hidden).toBeFalse();

    toggle().click();
    fixture.detectChanges();
    expect(setItem).toHaveBeenCalled();
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
  });
});
