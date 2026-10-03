import { Component, ChangeDetectorRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  COMPARISON_WIZARD_STEPS
} from '../model-comparison/model-comparison.component';
import { ReportDocumentsLauncherComponent } from '../report-pack/report-documents-launcher.component';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { BenchmarkComparisonState } from '../state/benchmark-comparison.state';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** The Model Comparison sub-tab: the launcher for the comparison wizard and the comparison reports. */
@Component({
  selector: 'app-benchmark-comparison-tab',
  standalone: true,
  imports: [
    CommonModule, ReportDocumentsLauncherComponent
  ],
  templateUrl: './benchmark-comparison-tab.component.html',
  styleUrls: ['./benchmark-comparison-tab.component.scss']
})
export class BenchmarkComparisonTabComponent implements OnInit {
  private readonly viewSync = inject(BenchmarkViewSync);
  readonly bridge = inject(BenchmarkShellBridge);
  private readonly workspace = inject(BenchmarkWorkspaceStore);
  readonly comparison = inject(BenchmarkComparisonState);
  private cdr = inject(ChangeDetectorRef);

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.comparison.restoreComparisonLauncherDisclosure();
    // The three lists the picker offers. No comparison is fetched here: an unattended request on
    // tab entry re-prices every entry for a selection the operator has not confirmed.
    this.workspace.loadHistory();
    this.workspace.loadRunGroups();
    this.workspace.loadSuites();
    this.comparison.restoreComparisonSelection();
    // The lists arrive asynchronously, so this indexes whatever is already in memory and runs
    // again from onComparisonSuiteChange as the scope narrows.
    this.comparison.loadComparabilityIndex();
  }

  /** The wizard's steps, which the launcher lists under the same titles as the wizard's stepper. */
  readonly comparisonWizardSteps = COMPARISON_WIZARD_STEPS;
}
