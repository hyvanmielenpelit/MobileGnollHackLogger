import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AdminBenchmarkService } from '../../../services/admin-benchmark.service';
import { recommendedRun, rootCauseControl, rootCauseControls } from '../report-pack/root-cause.testing';
import { BenchmarkShellBridge, LauncherSetupPrefill } from '../state/benchmark-shell-bridge.service';
import {
  ROOT_CAUSE_NO_CONTROLS_TEXT,
  ROOT_CAUSE_SEVERAL_MODELS_NOTE,
  RootCauseControlsComponent,
  RootCauseRewriteRequest,
  newRootCauseControls,
  rootCauseControlsLine
} from './root-cause-controls.component';

const LUNA = rootCauseControl();
const SONNET = rootCauseControl({
  modelLabel: 'Claude 5.5 Sonnet (medium)', provider: 'Anthropic', runId: 807, completedAtUtc: '2026-10-09T12:00:00Z'
});
const GEMINI = rootCauseControl({ modelLabel: 'Gemini 3.8 Flash (medium)', provider: 'Google', runId: 815 });

describe('RootCauseControlsComponent', () => {
  let fixture: ComponentFixture<RootCauseControlsComponent>;
  let component: RootCauseControlsComponent;
  let el: HTMLElement;
  let service: { getRunRootCauseControls: Mock; getBatteryRootCauseControls: Mock; getReportDocument: Mock };
  let bridge: BenchmarkShellBridge;
  let prefills: LauncherSetupPrefill[];

  beforeEach(async () => {
    service = {
      getRunRootCauseControls: vi.fn(() => of(rootCauseControls())),
      getBatteryRootCauseControls: vi.fn(() => of(rootCauseControls())),
      getReportDocument: vi.fn(() => of({ controls: [] }))
    };
    await TestBed.configureTestingModule({
      imports: [RootCauseControlsComponent],
      providers: [BenchmarkShellBridge, { provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
    bridge = TestBed.inject(BenchmarkShellBridge);
    prefills = [];
    bridge.prefillSetup$.subscribe(prefill => prefills.push(prefill));
    fixture = TestBed.createComponent(RootCauseControlsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
  });

  const text = (selector: string): string => (el.querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function render(kind: 'run' | 'battery', subjectId: number, rcaDocumentId: number | null = null): void {
    fixture.componentRef.setInput('subjectKind', kind);
    fixture.componentRef.setInput('subjectId', subjectId);
    fixture.componentRef.setInput('rcaDocumentId', rcaDocumentId);
    fixture.detectChanges();
  }

  it('words the controls line', () => {
    expect(rootCauseControlsLine([LUNA, SONNET])).toBe(
      'The Root Cause Analysis will use 2 controls: GPT-6 Luna (high), run #812 (2026-10-10); '
      + 'Claude 5.5 Sonnet (medium), run #807 (2026-10-09).');
    expect(rootCauseControlsLine([rootCauseControl({ runId: null, batteryRunId: 31 })]))
      .toBe('The Root Cause Analysis will use 1 control: GPT-6 Luna (high), battery run #31 (2026-10-10).');
    expect(rootCauseControlsLine([])).toBe(ROOT_CAUSE_NO_CONTROLS_TEXT);
    expect(newRootCauseControls([LUNA], [LUNA, GEMINI])).toEqual([GEMINI]);
  });

  it('before writing, names the controls the analysis will use and offers no control run', () => {
    service.getRunRootCauseControls.mockReturnValue(of(rootCauseControls({
      controls: [LUNA, SONNET], controlLimit: 'None', controlRun: null
    })));

    render('run', 55);

    expect(service.getRunRootCauseControls).toHaveBeenCalledWith(55);
    expect(service.getReportDocument).not.toHaveBeenCalled();
    expect(text('.rcc-line')).toBe(
      'The Root Cause Analysis will use 2 controls: GPT-6 Luna (high), run #812 (2026-10-10); '
      + 'Claude 5.5 Sonnet (medium), run #807 (2026-10-09).');
    expect(el.querySelector('.rcc-set-up')).toBeNull();
    expect(el.querySelector('.rcc-rewrite')).toBeNull();
    expect(text('.rcc-several')).toBe(ROOT_CAUSE_SEVERAL_MODELS_NOTE);
  });

  it('with no control, says so and sets up the control run through the bridge, starting nothing', () => {
    render('run', 55);

    expect(text('.rcc-line')).toBe(ROOT_CAUSE_NO_CONTROLS_TEXT);
    const button = el.querySelector<HTMLButtonElement>('.rcc-set-up')!;
    expect(button.classList).toContain('btn-ghost');
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    button.click();

    expect(prefills.length).toBe(1);
    expect(prefills[0].sourceLabel).toBe('Control run for run #55');
    expect(prefills[0].kind).toBe('control');
    expect(prefills[0].testedModelConfigurationIds).toEqual([2, 3]);
  });

  it('labels a battery run\'s control run by the battery run', () => {
    render('battery', 7);

    expect(service.getBatteryRootCauseControls).toHaveBeenCalledWith(7);
    el.querySelector<HTMLButtonElement>('.rcc-set-up')!.click();

    expect(prefills[0].sourceLabel).toBe('Control run for battery run #7');
  });

  it('keeps Set up a control run aria-disabled, with the reason, when the server proposes none', () => {
    service.getRunRootCauseControls.mockReturnValue(of(rootCauseControls({ controlRun: null })));
    render('run', 55);

    const button = el.querySelector<HTMLButtonElement>('.rcc-set-up')!;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(el.querySelector(`#${button.getAttribute('aria-describedby')}`)?.textContent).toContain('No control run can be proposed');
    button.click();
    expect(prefills).toEqual([]);
  });

  it('refuses the control run while the active sub-tab cannot be left', () => {
    render('run', 55);
    const release = bridge.setLeaveGuard(() => 'A batch check is running.');

    el.querySelector<HTMLButtonElement>('.rcc-set-up')!.click();
    fixture.detectChanges();

    expect(prefills).toEqual([]);
    expect(text('.rcc-refusal')).toBe('A batch check is running.');
    release();
  });

  it('after writing, offers Rewrite with controls only when more controls qualify than the document used', () => {
    service.getReportDocument.mockReturnValue(of({ controls: [LUNA] }));
    service.getRunRootCauseControls.mockReturnValue(of(rootCauseControls({ controls: [LUNA, GEMINI], controlLimit: 'None', controlRun: null })));
    const requests: RootCauseRewriteRequest[] = [];
    component.rewriteRequested.subscribe(request => requests.push(request));

    render('run', 55, 90);

    expect(service.getReportDocument).toHaveBeenCalledWith(90);
    expect(text('.rcc-used')).toContain('This Root Cause Analysis used 1 control: GPT-6 Luna (high), run #812');
    expect(text('.rcc-new')).toBe('New controls available since this document was written (run #815).');
    const rewrite = el.querySelector<HTMLButtonElement>('.rcc-rewrite')!;
    expect(rewrite.textContent).toContain('Rewrite with controls');
    rewrite.click();

    expect(requests).toEqual([{ documentId: 90, controls: [LUNA, GEMINI] }]);
  });

  it('offers no rewrite when the fresh set has no more controls than the document used', () => {
    service.getReportDocument.mockReturnValue(of({ controls: [LUNA, SONNET] }));
    service.getRunRootCauseControls.mockReturnValue(of(rootCauseControls({ controls: [LUNA, GEMINI], controlLimit: 'None', controlRun: null })));

    render('run', 55, 90);

    expect(el.querySelector('.rcc-new')).toBeNull();
    expect(el.querySelector('.rcc-rewrite')).toBeNull();
    expect(el.querySelector('.rcc-set-up')).toBeNull();
  });

  it('holds the rewrite while a job writes', () => {
    service.getReportDocument.mockReturnValue(of({ controls: [] }));
    service.getRunRootCauseControls.mockReturnValue(of(rootCauseControls({ controls: [LUNA], controlLimit: 'Moderate', controlRun: recommendedRun() })));
    const requests: RootCauseRewriteRequest[] = [];
    component.rewriteRequested.subscribe(request => requests.push(request));
    fixture.componentRef.setInput('busy', true);

    render('run', 55, 90);

    const rewrite = el.querySelector<HTMLButtonElement>('.rcc-rewrite')!;
    expect(rewrite.getAttribute('aria-disabled')).toBe('true');
    rewrite.click();
    expect(requests).toEqual([]);
  });

  it('says so when the controls cannot be looked up', () => {
    service.getRunRootCauseControls.mockReturnValue(throwError(() => ({ status: 500 })));

    render('run', 55);

    expect(text('.rcc-error')).toContain('could not be looked up');
    expect(el.querySelector('.rcc-retry')).not.toBeNull();
  });
});
