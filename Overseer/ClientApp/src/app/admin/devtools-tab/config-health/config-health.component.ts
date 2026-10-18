import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import {
  AdminDevtoolsService, ConfigHealthCheck, ConfigHealthReport, ConfigHealthStatus, ConfigSourceInfo
} from '../../../services/admin-devtools.service';
import { rovingTabIndex } from '../../../utils/roving-tab.util';

/** The remembered Problems / All checks choice, a per-viewer convenience. */
export const CONFIG_HEALTH_FILTER_STORAGE_KEY = 'overseer.admin.devtools.configHealth.filter';

export type ConfigHealthFilter = 'problems' | 'all';

/** A check with what the template shows for it. */
export interface ConfigHealthCheckView {
  check: ConfigHealthCheck;
  domId: string;
  statusLabel: string;
  /** `hidden` for a set value the server withholds (a sensitive key); `none` when nothing is set. */
  valueDisplay: 'shown' | 'hidden' | 'none';
  /** The remedy, only for a check that is not OK and has one. */
  remedy: string | null;
}

export interface ConfigHealthCategoryGroup {
  category: string;
  headingId: string;
  checks: ConfigHealthCheckView[];
}

export interface ConfigSourceRow {
  name: string;
  kind: string;
  fileText: string;
  /** A status glyph beside the file text, or null for a plain fact. */
  fileStatus: ConfigHealthStatus | null;
  keyCount: number;
}

export interface ConfigHealthTile {
  status: ConfigHealthStatus;
  label: string;
  count: number;
}

const STATUS_LABELS: Record<ConfigHealthStatus, string> = {
  ok: 'OK',
  warning: 'Warning',
  error: 'Error',
  info: 'Info'
};

const NOT_SET = 'Not set';

function isProblem(check: ConfigHealthCheck): boolean {
  return check.status === 'warning' || check.status === 'error';
}

function isUserSecrets(source: ConfigSourceInfo): boolean {
  return `${source.name} ${source.kind}`.toLowerCase().includes('user secrets');
}

/**
 * Services > Configuration Health: every configuration check the server runs, grouped by
 * category, with the configuration sources it loaded and a Problems / All checks filter.
 */
@Component({
  selector: 'app-config-health',
  templateUrl: './config-health.component.html',
  styleUrl: './config-health.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet]
})
export class ConfigHealthComponent implements OnInit {
  private devtools = inject(AdminDevtoolsService);
  private cdr = inject(ChangeDetectorRef);
  private destroyRef = inject(DestroyRef);
  private request: Subscription | null = null;
  private timeFormat = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' });

  readonly filters: readonly ConfigHealthFilter[] = ['problems', 'all'];

  report: ConfigHealthReport | null = null;
  loading = false;
  loadError: string | null = null;
  checkedAtText: string | null = null;

  tiles: ConfigHealthTile[] = [];
  sourceRows: ConfigSourceRow[] = [];
  visibleGroups: ConfigHealthCategoryGroup[] = [];
  problemCount = 0;
  allCount = 0;

  /** The viewer's explicit choice; null until one is made or read from storage. */
  private chosenFilter: ConfigHealthFilter | null = this.readStoredFilter();
  private allGroups: ConfigHealthCategoryGroup[] = [];

  /** The chosen filter, else Problems when there are any and All checks when there are none. */
  get filter(): ConfigHealthFilter {
    return this.chosenFilter ?? (this.problemCount > 0 ? 'problems' : 'all');
  }

  ngOnInit(): void {
    this.load();
  }

  recheck(): void {
    if (this.loading) return;
    this.load();
  }

  selectFilter(filter: ConfigHealthFilter): void {
    this.chosenFilter = filter;
    this.storeFilter(filter);
    this.rebuildVisibleGroups();
    this.cdr.markForCheck();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onFilterKeydown(event: KeyboardEvent, index: number): void {
    const target = rovingTabIndex(event, index, this.filters.length);
    if (target === null) return;
    const next = this.filters[target];
    this.selectFilter(next);
    this.cdr.detectChanges();
    document.getElementById(`ch-filter-${next}`)?.focus();
  }

  filterLabel(filter: ConfigHealthFilter): string {
    return filter === 'problems' ? `Problems (${this.problemCount})` : `All checks (${this.allCount})`;
  }

  private load(): void {
    this.request?.unsubscribe();
    this.loading = true;
    this.loadError = null;
    this.cdr.markForCheck();
    this.request = this.devtools.getConfigHealth()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: report => {
          this.loading = false;
          this.applyReport(report);
          this.cdr.markForCheck();
        },
        error: (err: unknown) => {
          this.loading = false;
          this.loadError = `The configuration health report could not be loaded.${this.errorDetail(err)}`;
          this.cdr.markForCheck();
        }
      });
  }

  private applyReport(report: ConfigHealthReport): void {
    this.report = report;
    const generated = new Date(report.generatedAtUtc);
    this.checkedAtText = this.timeFormat.format(Number.isNaN(generated.getTime()) ? new Date() : generated);
    this.tiles = [
      { status: 'ok', label: 'OK', count: report.summary.ok },
      { status: 'warning', label: 'Warnings', count: report.summary.warning },
      { status: 'error', label: 'Errors', count: report.summary.error },
      { status: 'info', label: 'Info', count: report.summary.info }
    ];
    this.sourceRows = this.buildSourceRows(report);
    this.allCount = report.checks.length;
    this.problemCount = report.checks.filter(isProblem).length;
    this.allGroups = this.groupByCategory(report.checks);
    this.rebuildVisibleGroups();
  }

  /** Categories in the order the report first names them; checks in report order within each. */
  private groupByCategory(checks: ConfigHealthCheck[]): ConfigHealthCategoryGroup[] {
    const groups = new Map<string, ConfigHealthCategoryGroup>();
    checks.forEach((check, index) => {
      let group = groups.get(check.category);
      if (!group) {
        group = { category: check.category, headingId: `ch-category-${groups.size}`, checks: [] };
        groups.set(check.category, group);
      }
      group.checks.push(this.toView(check, index));
    });
    return [...groups.values()];
  }

  private toView(check: ConfigHealthCheck, index: number): ConfigHealthCheckView {
    const hasValue = check.value !== null && check.value !== '';
    const providerSet = check.source !== null && check.source !== NOT_SET;
    const remedy = check.remedy?.trim() ?? '';
    return {
      check,
      domId: `ch-check-${index}`,
      statusLabel: STATUS_LABELS[check.status] ?? check.status,
      valueDisplay: hasValue ? 'shown' : check.value === null && providerSet ? 'hidden' : 'none',
      remedy: check.status !== 'ok' && remedy !== '' ? remedy : null
    };
  }

  private rebuildVisibleGroups(): void {
    if (this.filter === 'all') {
      this.visibleGroups = this.allGroups;
      return;
    }
    this.visibleGroups = this.allGroups
      .map(group => ({ ...group, checks: group.checks.filter(view => isProblem(view.check)) }))
      .filter(group => group.checks.length > 0);
  }

  /** The loaded sources in the order the server lists them, lowest precedence first. */
  private buildSourceRows(report: ConfigHealthReport): ConfigSourceRow[] {
    const rows: ConfigSourceRow[] = report.sources.map(source => ({
      name: source.name,
      kind: source.kind,
      keyCount: source.keyCount,
      ...this.fileCell(source, report)
    }));
    if (!report.userSecretsExpected && !report.sources.some(isUserSecrets)) {
      rows.push({
        name: 'User Secrets',
        kind: 'Not loaded',
        keyCount: 0,
        fileText: `Not used in ${report.environmentName}`,
        fileStatus: 'info'
      });
    }
    return rows;
  }

  private fileCell(source: ConfigSourceInfo, report: ConfigHealthReport): Pick<ConfigSourceRow, 'fileText' | 'fileStatus'> {
    if (source.fileExists === null) return { fileText: 'Not a file', fileStatus: null };
    if (source.fileExists) return { fileText: 'Found', fileStatus: null };
    if (isUserSecrets(source)) {
      return report.userSecretsExpected
        ? { fileText: 'Missing', fileStatus: 'warning' }
        : { fileText: `Not used in ${report.environmentName}`, fileStatus: 'info' };
    }
    return { fileText: 'Not found', fileStatus: null };
  }

  private errorDetail(err: unknown): string {
    const e = err as { error?: { message?: unknown } | null; message?: unknown } | null;
    const serverMessage = e?.error?.message;
    const clientMessage = e?.message;
    const message = typeof serverMessage === 'string' ? serverMessage
      : typeof clientMessage === 'string' ? clientMessage : '';
    return message ? ` ${message}` : '';
  }

  private readStoredFilter(): ConfigHealthFilter | null {
    try {
      const stored = localStorage.getItem(CONFIG_HEALTH_FILTER_STORAGE_KEY);
      return stored === 'problems' || stored === 'all' ? stored : null;
    } catch {
      return null;   // Private mode or blocked storage: the default applies.
    }
  }

  private storeFilter(filter: ConfigHealthFilter): void {
    try {
      localStorage.setItem(CONFIG_HEALTH_FILTER_STORAGE_KEY, filter);
    } catch {
      // Blocked storage: the choice holds for this visit only.
    }
  }
}
