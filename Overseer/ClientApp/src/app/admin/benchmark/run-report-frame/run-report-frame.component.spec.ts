import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

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
    expect(q('section.rrf-main .t-main').textContent).toBe('Main content');
    expect(q('section.rrf-aside .t-aside').textContent).toBe('Aside content');
  });

  it('renders the two columns by default, with no single body and no tab row', () => {
    expect(q('.rrf-columns')).not.toBeNull();
    expect(host.querySelector('.rrf-single')).toBeNull();
    expect(q('app-run-report-frame').classList.contains('rrf-layout-single')).toBeFalse();
    expect(getComputedStyle(q('.rrf-tabs')).display).toBe('none');
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

  it('keeps the header sticky', () => {
    setWidth(700);
    expect(getComputedStyle(q('.rrf-header')).position).toBe('sticky');

    setWidth(1200);
    expect(getComputedStyle(q('.rrf-top')).position).toBe('sticky');
  });
});

@Component({
  standalone: true,
  imports: [RunReportFrameComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="shell" [style.width.px]="width" style="height: 640px; display: flex; flex-direction: column;">
      <app-run-report-frame layout="single">
        <h3 runReportHeader class="t-title">Run #7</h3>
        <div runReportTabs class="t-tabs" role="tablist" aria-label="Run report sections">
          <button type="button" role="tab">Summary</button>
        </div>
        <div runReportBody class="t-body" style="height: 3000px;">Body content</div>
      </app-run-report-frame>
    </div>
  `
})
class SingleHostComponent {
  width = 1200;
}

describe('RunReportFrameComponent in single layout', () => {
  let fixture: ComponentFixture<SingleHostComponent>;
  let host: HTMLElement;

  const q = (selector: string): HTMLElement => host.querySelector<HTMLElement>(selector)!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SingleHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(SingleHostComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  it('renders the body slot in .rrf-single, with no columns, and sets the host class', () => {
    expect(q('.rrf-body > .rrf-single > .t-body').textContent).toBe('Body content');
    expect(host.querySelector('.rrf-columns')).toBeNull();
    expect(host.querySelector('.rrf-main, .rrf-aside')).toBeNull();
    expect(q('app-run-report-frame').classList.contains('rrf-layout-single')).toBeTrue();
  });

  it('puts the tab row under the header, inside the top block', () => {
    const tabs = q('.rrf-top > .rrf-tabs');

    expect(tabs.querySelector('.t-tabs')).not.toBeNull();
    expect(tabs.previousElementSibling).toBe(q('.rrf-header'));
    expect(getComputedStyle(tabs).display).not.toBe('none');
  });

  it('keeps the top block in place and scrolls the body, at every width', () => {
    for (const width of [1200, 500]) {
      fixture.componentInstance.width = width;
      fixture.detectChanges();

      expect(getComputedStyle(q('.rrf-top')).display).withContext(`${width}`).toBe('block');
      expect(getComputedStyle(q('app-run-report-frame')).overflowY).withContext(`${width}`).toBe('hidden');
      expect(getComputedStyle(q('.rrf-body')).overflowY).withContext(`${width}`).toBe('auto');
    }
  });

  it('scrollBodyToTop() resets the body scroll', () => {
    const body = q('.rrf-body');
    body.scrollTop = 400;
    expect(body.scrollTop).toBeGreaterThan(0);

    const frame = fixture.debugElement.query(By.directive(RunReportFrameComponent)).componentInstance as RunReportFrameComponent;
    frame.scrollBodyToTop();

    expect(body.scrollTop).toBe(0);
  });
});
