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
});
