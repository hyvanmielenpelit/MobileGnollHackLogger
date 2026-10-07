import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, forkJoin } from 'rxjs';

import { AdminChatConsistencyService, ccErrorText } from '../../../services/admin-chat-consistency.service';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { CcAnalysisWizardComponent } from './analysis-wizard/analysis-wizard.component';
import { CcAnnotationsPanelComponent } from './annotations/annotations-panel.component';
import { endOfUtcDay, startOfUtcDay } from './chat-consistency-format';
import {
  CcAnalysisResult,
  CcAnalysisSummary,
  CcModelAxis,
  CcOpenDocumentsRequest,
  CcRunRow,
  CcTimeline
} from './chat-consistency.models';
import { CcSavedAnalysesComponent } from './saved-analyses/saved-analyses.component';
import { CcDayRange, CcTimelinePanelComponent } from './timeline-panel/timeline-panel.component';

/** The tab's sections, each a native disclosure. */
export type CcSection = 'timeline' | 'analyze' | 'saved' | 'annotations';

/**
 * The Chat Consistency sub-tab: whether the Overseer chat with one model stayed the same over time.
 * The Timeline shows the model's runs and their eligibility; *Analyze chat consistency* compares two
 * periods under Protocol V1 and writes its reports; the saved analyses and the annotations follow.
 *
 * It owns the subject, the date range and their data, and performs the run actions the sections ask
 * for: anchors through the API, *Repeat this run's setup* and *Open run report* through the shell.
 */
@Component({
  selector: 'app-chat-consistency-tab',
  standalone: true,
  imports: [CcTimelinePanelComponent, CcAnalysisWizardComponent, CcSavedAnalysesComponent, CcAnnotationsPanelComponent],
  templateUrl: './chat-consistency-tab.component.html',
  styleUrls: ['./chat-consistency-tab.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChatConsistencyTabComponent implements OnInit, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly bridge = inject(BenchmarkShellBridge);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  readonly workspace = inject(BenchmarkWorkspaceStore);

  /** The Download Center on an analysis's documents. */
  @Output() readonly openDocuments = new EventEmitter<CcOpenDocumentsRequest>();

  @ViewChild(CcAnalysisWizardComponent) wizard?: CcAnalysisWizardComponent;

  readonly open: Record<CcSection, boolean> = { timeline: true, analyze: false, saved: false, annotations: false };

  axes: CcModelAxis[] = [];
  axesLoading = false;
  axesError: string | null = null;

  selectedKey: string | null = null;
  range: CcDayRange = { fromDay: '', toDay: '' };
  timeline: CcTimeline | null = null;
  rows: CcRunRow[] = [];
  subjectLoading = false;
  timelineError: string | null = null;
  runsError: string | null = null;

  anchorBusy = new Set<number>();
  anchorError: string | null = null;
  announcement = '';

  analyses: CcAnalysisSummary[] = [];
  analysesLoading = false;
  analysesError: string | null = null;
  openingId: number | null = null;
  openError: string | null = null;

  private axesSub: Subscription | null = null;
  private subjectSub: Subscription | null = null;
  private analysesSub: Subscription | null = null;
  private openSub: Subscription | null = null;
  private readonly anchorSubs = new Map<number, Subscription>();

  constructor() {
    // The configurations the pickers offer arrive through the shell; OnPush needs telling.
    inject(BenchmarkViewSync).changed$.pipe(takeUntilDestroyed()).subscribe(() => this.cdr.markForCheck());
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.loadAxes();
    this.loadAnalyses();
  }

  ngOnDestroy(): void {
    for (const sub of [this.axesSub, this.subjectSub, this.analysesSub, this.openSub, ...this.anchorSubs.values()]) {
      sub?.unsubscribe();
    }
  }

  get selectedAxis(): CcModelAxis | null {
    return this.axes.find(axis => axis.key === this.selectedKey) ?? null;
  }

  /** The Timeline summary's read-out while the section is closed. */
  get timelineReadout(): string {
    const axis = this.selectedAxis;
    if (!axis) return 'No model chosen';
    const count = this.timeline?.points.length ?? axis.runCount;
    return `${axis.displayName} · ${count} ${count === 1 ? 'run' : 'runs'}`;
  }

  onToggle(section: CcSection, event: Event): void {
    this.open[section] = (event.target as HTMLDetailsElement).open;
  }

  // --- Loading ---

  loadAxes(): void {
    this.axesLoading = true;
    this.axesError = null;
    this.axesSub?.unsubscribe();
    this.axesSub = this.service.listModels().subscribe({
      next: axes => {
        this.axesLoading = false;
        this.axes = axes;
        this.cdr.markForCheck();
      },
      error: err => {
        this.axesLoading = false;
        this.axesError = ccErrorText(err, 'The models could not be loaded.');
        this.cdr.markForCheck();
      }
    });
  }

  loadAnalyses(): void {
    this.analysesLoading = true;
    this.analysesError = null;
    this.analysesSub?.unsubscribe();
    this.analysesSub = this.service.listAnalyses().subscribe({
      next: analyses => {
        this.analysesLoading = false;
        this.analyses = analyses;
        this.cdr.markForCheck();
      },
      error: err => {
        this.analysesLoading = false;
        this.analysesError = ccErrorText(err, 'The saved analyses could not be loaded.');
        this.cdr.markForCheck();
      }
    });
  }

  selectModel(key: string): void {
    if (key === this.selectedKey) return;
    this.selectedKey = key;
    this.timeline = null;
    this.rows = [];
    this.anchorError = null;
    this.announcement = '';
    this.loadSubject();
  }

  setRange(range: CcDayRange): void {
    this.range = range;
    this.loadSubject();
  }

  /** The timeline and the run table of the subject over the range, together. */
  loadSubject(): void {
    const key = this.selectedKey;
    this.subjectSub?.unsubscribe();
    if (!key) return;
    const from = this.range.fromDay ? startOfUtcDay(this.range.fromDay) : null;
    const to = this.range.toDay ? endOfUtcDay(this.range.toDay) : null;
    this.subjectLoading = true;
    this.timelineError = null;
    this.runsError = null;
    this.cdr.markForCheck();
    this.subjectSub = forkJoin({
      timeline: this.service.getTimeline(key, from, to),
      rows: this.service.getRuns(key, from, to)
    }).subscribe({
      next: ({ timeline, rows }) => {
        if (this.selectedKey !== key) return;
        this.subjectLoading = false;
        this.timeline = timeline;
        this.rows = rows;
        this.cdr.markForCheck();
      },
      error: err => {
        if (this.selectedKey !== key) return;
        this.subjectLoading = false;
        this.timelineError = ccErrorText(err, 'The timeline could not be loaded.');
        this.runsError = this.timelineError;
        this.cdr.markForCheck();
      }
    });
  }

  // --- Run actions ---

  repeatSetup(runId: number): void {
    this.bridge.repeatRunSetup(runId);
  }

  openRunReport(runId: number): void {
    this.bridge.viewRunDetail(runId);
  }

  /** Marks or unmarks the run as the grader anchor, and updates the row and its timeline point in place. */
  toggleAnchor(row: CcRunRow): void {
    if (this.anchorBusy.has(row.runId)) return;
    const isAnchor = !row.isAnchor;
    this.anchorBusy = new Set([...this.anchorBusy, row.runId]);
    this.anchorError = null;
    this.announcement = '';
    this.cdr.markForCheck();
    this.anchorSubs.get(row.runId)?.unsubscribe();
    this.anchorSubs.set(row.runId, this.service.setAnchor(row.runId, isAnchor).subscribe({
      next: response => {
        this.clearAnchorBusy(row.runId);
        const marked = response?.isAnchor ?? isAnchor;
        this.rows = this.rows.map(r => r.runId === row.runId ? { ...r, isAnchor: marked } : r);
        if (this.timeline) {
          this.timeline = {
            ...this.timeline,
            points: this.timeline.points.map(p => p.runId === row.runId ? { ...p, isAnchor: marked } : p)
          };
        }
        this.announcement = marked ? `Run #${row.runId} is the grader anchor.` : `Run #${row.runId} is no longer an anchor.`;
        this.cdr.markForCheck();
      },
      error: err => {
        this.clearAnchorBusy(row.runId);
        this.anchorError = `The anchor of run #${row.runId} could not be saved: ${ccErrorText(err, 'the request failed.')}`;
        this.cdr.markForCheck();
      }
    }));
  }

  private clearAnchorBusy(runId: number): void {
    const next = new Set(this.anchorBusy);
    next.delete(runId);
    this.anchorBusy = next;
    this.anchorSubs.delete(runId);
  }

  // --- Analyses ---

  onAnalysisSaved(result: CcAnalysisResult): void {
    this.loadAnalyses();
    this.announcement = result.analysisId !== null ? `Analysis #${result.analysisId} was saved.` : '';
  }

  /** Opens a saved analysis on its results, switching the subject to its model first. */
  openAnalysis(id: number): void {
    this.openingId = id;
    this.openError = null;
    this.cdr.markForCheck();
    this.openSub?.unsubscribe();
    this.openSub = this.service.getAnalysis(id).subscribe({
      next: result => {
        this.openingId = null;
        if (result.subject.key && result.subject.key !== this.selectedKey) {
          this.selectModel(result.subject.key);
        }
        this.open.analyze = true;
        this.cdr.detectChanges();
        this.wizard?.showResult(result);
        this.host.nativeElement.querySelector('#cc-section-analyze')?.scrollIntoView({ block: 'start' });
        this.cdr.markForCheck();
      },
      error: err => {
        this.openingId = null;
        this.openError = ccErrorText(err, 'The analysis could not be opened.');
        this.cdr.markForCheck();
      }
    });
  }

  onAnalysisDeleted(id: number): void {
    this.analyses = this.analyses.filter(analysis => analysis.id !== id);
    this.cdr.markForCheck();
  }

  onRunsChanged(): void {
    this.loadSubject();
  }

  onAnnotationsChanged(): void {
    this.loadSubject();
  }
}
