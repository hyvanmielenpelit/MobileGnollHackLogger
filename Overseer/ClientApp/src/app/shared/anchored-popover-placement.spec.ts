import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ConfigFilterComponent } from '../admin/config-filter/config-filter.component';
import { createEmptyFilter } from '../admin/config-filter/config-filter.model';
import { NATIVE_ANCHOR_CLASS, markNativeAnchorPositioning } from '../utils/polyfills.util';
import { FilterFacetComponent, FilterFacetOption } from './data-table/filter-facet.component';

@Component({
  standalone: true,
  imports: [FilterFacetComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-filter-facet facetId="geo-facet" label="Document" [options]="options"></app-filter-facet>`
})
class GeoFacetHostComponent {
  options: FilterFacetOption[] = [
    { value: 'Executive Summary', label: 'Executive Summary', count: 4 },
    { value: 'Run report', label: 'Run report', count: 1 },
    { value: 'Tool-call log', label: 'Tool-call log', count: 0 },
    { value: 'Diagnostics', label: 'Diagnostics', count: 2 },
    { value: 'Assessments', label: 'Assessments', count: 7 },
    { value: 'Multi-Run Analysis', label: 'Multi-Run Analysis', count: 3 }
  ];
}

/** The shape `app-info-tip trigger="click"` renders, with the Number of Runs content. */
const INFO_MARKUP = `
  <button type="button" data-geo="trigger" class="gh-info-btn gh-info-btn--click" aria-label="About Number of Runs"
          style="anchor-name: --geo-info">
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle></svg>
  </button>
  <div popover="auto" class="gh-info-popup" style="position-anchor: --geo-info">
    <div class="gh-info-popup-title">Number of Runs</div>
    <div class="gh-info-popup-body">
      <p>Answers vary from run to run, so the scores of a single run include run-to-run noise.</p>
      <dl>
        <div>
          <dt><span class="gh-info-term">One run</span><span class="gh-info-badge">Default</span></dt>
          <dd>For a first look at a model, a check that a change works, or the lowest cost. Treat a small
            difference between two single runs with caution: it may be noise.</dd>
        </div>
        <div>
          <dt><span class="gh-info-term">Two or more runs</span></dt>
          <dd>Up to 20, run one at a time as a replicate set. Multi-Run Analysis reports their mean and, from
            three runs, a reproducibility interval. Use it when the difference you are looking for is small, such
            as between two close models.</dd>
        </div>
      </dl>
      <p>Cost and duration grow with each run; the Series Projection estimates both once two or more runs are
        set.</p>
    </div>
  </div>`;

/**
 * The Run report's Re-run menu: four items, two of them unavailable with a reason line. Every line
 * fits the 14rem minimum width, so the popover is equally wide wherever it is placed.
 */
const ACTION_MARKUP = `
  <button type="button" data-geo="trigger" class="btn-ghost" style="anchor-name: --geo-action">Re-run</button>
  <div popover="auto" class="gh-action-popover" role="group" aria-label="Re-run" style="position-anchor: --geo-action">
    <button type="button" class="gh-action-popover-item" aria-disabled="true">Re-score run
      <span class="gh-action-popover-item-reason">Re-scoring is in progress.</span></button>
    <button type="button" class="gh-action-popover-item">Re-run final synthesis</button>
    <button type="button" class="gh-action-popover-item">Retry failed assessments</button>
    <button type="button" class="gh-action-popover-item" aria-disabled="true">Re-run failed questions
      <span class="gh-action-popover-item-reason">A retry is already running.</span></button>
  </div>`;

type Spot = 'top-left' | 'bottom-left' | 'bottom-right' | 'middle' | 'lower-middle' | 'upper-middle';

interface MeasureOptions {
  native: boolean;
  triggerWidth?: number;
  scrolled?: boolean;
}

interface Measurement {
  popover: DOMRect;
  trigger: DOMRect;
  clientWidth: number;
  clientHeight: number;
}

const TOLERANCE = 0.5;
/** toBeCloseTo precision: 0 decimal digits passes within 0.5. */
const WITHIN_HALF_PX = 0;

/**
 * The CSS the test page has loaded (styles.scss and the live components' styles), for an iframe.
 * Web fonts and imports are left out, so that both measurements of a case lay out with the same
 * fallback fonts; animations are switched off, so that an opening slide is not read as placement.
 */
function collectCss(): string {
  const parts: string[] = [];
  for (const sheet of Array.from(document.styleSheets)) {
    let rules: CSSRuleList;
    try {
      rules = sheet.cssRules;
    } catch {
      continue;
    }
    for (const rule of Array.from(rules)) {
      if (rule instanceof CSSFontFaceRule || rule instanceof CSSImportRule) {
        continue;
      }
      parts.push(rule.cssText);
    }
  }
  parts.push('*, *::before, *::after { animation: none !important; transition: none !important; }');
  return parts.join('\n');
}

function spotPosition(spot: Spot, w: number, h: number, tw: number, th: number): [number, number] {
  switch (spot) {
    case 'top-left': return [8, 8];
    case 'bottom-left': return [8, h - th - 8];
    case 'bottom-right': return [w - tw - 8, h - th - 8];
    case 'middle': return [(w - tw) / 2, (h - th) / 2];
    case 'lower-middle': return [(w - tw) / 2, 0.62 * h];
    case 'upper-middle': return [(w - tw) / 2, 0.3 * h];
  }
}

/**
 * Lays `source` out in an iframe of w×h CSS px with its trigger at `spot`, opens its popover and
 * returns both boxes. With `native` the document carries the gate class, so the native-only rules
 * apply; without it, the rules the polyfill and older engines get.
 */
function measure(source: HTMLElement, w: number, h: number, spot: Spot, options: MeasureOptions): Measurement {
  const css = collectCss();
  expect(css, 'the global stylesheet is loaded').toContain('.gh-action-popover');

  const iframe = document.createElement('iframe');
  iframe.style.cssText = `position: fixed; left: 0; top: 0; width: ${w}px; height: ${h}px; border: 0`;
  document.body.appendChild(iframe);
  let popover: HTMLElement | null = null;
  try {
    const doc = iframe.contentDocument!;
    const filler = options.scrolled ? '<div style="height:3000px"></div>' : '';
    doc.open();
    doc.write(
      `<!doctype html><html${options.native ? ` class="${NATIVE_ANCHOR_CLASS}"` : ''}>` +
      `<head><style>${css}</style></head><body style="margin:0">` +
      filler + source.outerHTML + filler + '</body></html>');
    doc.close();

    const trigger = doc.querySelector<HTMLElement>('[data-geo="trigger"], #geo-facet-trigger, #config-filter-trigger')!;
    popover = doc.querySelector<HTMLElement>('[popover]')!;
    expect(trigger, 'trigger').toBeTruthy();
    expect(popover, 'popover').toBeTruthy();

    const style = trigger.style;
    if (options.triggerWidth !== undefined) {
      style.setProperty('width', `${options.triggerWidth}px`, 'important');
      style.setProperty('min-width', '0', 'important');
      style.setProperty('padding', '0', 'important');
      style.setProperty('box-sizing', 'border-box', 'important');
      style.setProperty('overflow', 'hidden', 'important');
    }
    if (options.scrolled) {
      const box = trigger.getBoundingClientRect();
      const [, y] = spotPosition(spot, w, h, box.width, box.height);
      iframe.contentWindow!.scrollTo(0, box.top - y);
    } else {
      style.setProperty('position', 'fixed', 'important');
      style.setProperty('margin', '0', 'important');
      style.setProperty('left', '0', 'important');
      style.setProperty('top', '0', 'important');
      const box = trigger.getBoundingClientRect();
      const [x, y] = spotPosition(spot, w, h, box.width, box.height);
      style.setProperty('left', `${x}px`, 'important');
      style.setProperty('top', `${y}px`, 'important');
    }

    popover.showPopover();
    return {
      popover: popover.getBoundingClientRect(),
      trigger: trigger.getBoundingClientRect(),
      clientWidth: doc.documentElement.clientWidth,
      clientHeight: doc.documentElement.clientHeight
    };
  } finally {
    try {
      popover?.hidePopover();
    } catch {
      // Not open.
    }
    iframe.remove();
  }
}

function isInside(m: Measurement): boolean {
  const box = m.popover;
  return box.left >= -TOLERANCE && box.top >= -TOLERANCE &&
    box.right <= m.clientWidth + TOLERANCE && box.bottom <= m.clientHeight + TOLERANCE;
}

function boxText(box: DOMRect): string {
  return [box.left, box.top, box.right, box.bottom].map(n => Math.round(n)).join(',');
}

describe('Anchored popover placement', () => {
  let staticHost: HTMLElement;
  let facetFixture: ComponentFixture<GeoFacetHostComponent>;
  let configFixture: ComponentFixture<ConfigFilterComponent>;

  const sources: Record<'info' | 'action' | 'facet' | 'config', () => HTMLElement> = {
    info: () => staticHost.querySelector<HTMLElement>('.geo-info')!,
    action: () => staticHost.querySelector<HTMLElement>('.geo-action')!,
    facet: () => facetFixture.nativeElement.querySelector('app-filter-facet') as HTMLElement,
    config: () => configFixture.nativeElement as HTMLElement
  };

  beforeEach(async () => {
    staticHost = document.createElement('div');
    staticHost.innerHTML = `<div class="geo-info">${INFO_MARKUP}</div><div class="geo-action">${ACTION_MARKUP}</div>`;
    document.body.appendChild(staticHost);

    await TestBed.configureTestingModule({ imports: [GeoFacetHostComponent, ConfigFilterComponent] }).compileComponents();
    facetFixture = TestBed.createComponent(GeoFacetHostComponent);
    facetFixture.detectChanges();
    configFixture = TestBed.createComponent(ConfigFilterComponent);
    configFixture.componentRef.setInput('filter', createEmptyFilter());
    configFixture.componentRef.setInput('providers', ['OpenAI', 'Anthropic', 'Google']);
    configFixture.detectChanges();
  });

  afterEach(() => {
    staticHost.remove();
    facetFixture.destroy();
    configFixture.destroy();
  });

  const popovers = ['info', 'action', 'facet', 'config'] as const;
  const viewports: [number, number][] = [[375, 700], [1000, 655], [1280, 800], [1000, 300]];
  const spots: Spot[] = ['top-left', 'bottom-left', 'bottom-right', 'middle', 'lower-middle'];

  for (const kind of popovers) {
    for (const [w, h] of viewports) {
      for (const spot of spots) {
        it(`keeps the ${kind} popover inside a ${w}×${h} viewport with its trigger ${spot}`, () => {
          const native = measure(sources[kind](), w, h, spot, { native: true });
          expect(isInside(native), `native box ${boxText(native.popover)}`).toBe(true);

          const today = measure(sources[kind](), w, h, spot, { native: false });
          if (isInside(today)) {
            for (const side of ['left', 'top', 'right', 'bottom'] as const) {
              expect(native.popover[side], `${side}, unchanged where it fits today`).toBeCloseTo(today.popover[side], WITHIN_HALF_PX);
            }
          }
        });
      }
    }
  }

  it('keeps the ordinary placement when there is room', () => {
    // The info popup is about 410 px tall, more than the room below a trigger at mid-height.
    const info = measure(sources.info(), 1280, 800, 'upper-middle', { native: true });
    expect(info.popover.top, 'info top').toBeCloseTo(info.trigger.bottom + 6, WITHIN_HALF_PX);
    expect(info.popover.right, 'info right').toBeCloseTo(info.trigger.right, WITHIN_HALF_PX);

    const action = measure(sources.action(), 1280, 800, 'middle', { native: true });
    expect(action.popover.top, 'action top').toBeCloseTo(action.trigger.bottom + 6, WITHIN_HALF_PX);
    expect(action.popover.right, 'action right').toBeCloseTo(action.trigger.right, WITHIN_HALF_PX);

    const facet = measure(sources.facet(), 1280, 800, 'middle', { native: true });
    expect(facet.popover.top, 'facet top').toBeCloseTo(facet.trigger.bottom + 6, WITHIN_HALF_PX);
    expect(facet.popover.left, 'facet left').toBeCloseTo(facet.trigger.left, WITHIN_HALF_PX);

    const config = measure(sources.config(), 1280, 800, 'middle', { native: true });
    expect(config.popover.top, 'config top').toBeCloseTo(config.trigger.bottom + 8, WITHIN_HALF_PX);
    expect(config.popover.right, 'config right').toBeCloseTo(config.trigger.right, WITHIN_HALF_PX);
  });

  it('spans a 375 px phone with the config filter sheet', () => {
    const config = measure(sources.config(), 375, 700, 'middle', { native: true });
    expect(config.popover.left).toBeCloseTo(0, WITHIN_HALF_PX);
    expect(config.popover.right).toBeCloseTo(375, WITHIN_HALF_PX);
  });

  it('does not stretch a popover that fits by height but by neither alignment', () => {
    for (const kind of ['action', 'facet'] as const) {
      const native = measure(sources[kind](), 375, 700, 'middle', { native: true, triggerWidth: 40 });
      const today = measure(sources[kind](), 375, 700, 'middle', { native: false, triggerWidth: 40 });
      expect(isInside(native), `${kind} native box ${boxText(native.popover)}`).toBe(true);
      expect(native.popover.width, `${kind} width, so that the heights compare`).toBeCloseTo(today.popover.width, WITHIN_HALF_PX);
      expect(native.popover.height, `${kind} height`).toBeCloseTo(today.popover.height, WITHIN_HALF_PX);
    }
  });

  it('keeps the config filter inside a scrolled document', () => {
    const config = measure(sources.config(), 1000, 655, 'bottom-left', { native: true, scrolled: true });
    expect(isInside(config), `box ${boxText(config.popover)}`).toBe(true);
  });

  it('marks the document only when anchor positioning is native', () => {
    const root = document.createElement('html');
    markNativeAnchorPositioning(root);
    expect(root.classList.contains(NATIVE_ANCHOR_CLASS)).toBe(true);

    const classList = document.createElement('div').classList;
    markNativeAnchorPositioning({ style: {}, classList } as unknown as HTMLElement);
    expect(classList.contains(NATIVE_ANCHOR_CLASS)).toBe(false);
  });
});
