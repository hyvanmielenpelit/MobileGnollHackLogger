import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  CONFIG_HEALTH_FILTER_STORAGE_KEY, ConfigHealthComponent
} from './config-health.component';
import {
  AdminDevtoolsService, ConfigHealthCheck, ConfigHealthReport
} from '../../../services/admin-devtools.service';
import { configureAdminTestBed } from '../../admin.component.testing';

function check(overrides: Partial<ConfigHealthCheck> = {}): ConfigHealthCheck {
  return {
    id: 'check',
    category: 'Tool corpora',
    title: 'Check',
    keys: ['SourceCodePath'],
    status: 'ok',
    source: 'appsettings.json',
    value: 'C:\\src\\GnollHack',
    summary: 'The directory exists.',
    remedy: 'Set it.',
    ...overrides
  };
}

function buildReport(checks: ConfigHealthCheck[], overrides: Partial<ConfigHealthReport> = {}): ConfigHealthReport {
  const count = (status: ConfigHealthCheck['status']) => checks.filter(c => c.status === status).length;
  return {
    generatedAtUtc: '2026-10-18T12:34:56Z',
    environmentName: 'Development',
    userSecretsExpected: true,
    sources: [
      { name: 'appsettings.json', kind: 'Json', fileExists: true, keyCount: 40 },
      { name: 'User Secrets', kind: 'Json', fileExists: true, keyCount: 12 },
      { name: 'Environment variables', kind: 'EnvironmentVariables', fileExists: null, keyCount: 80 }
    ],
    summary: { ok: count('ok'), warning: count('warning'), error: count('error'), info: count('info') },
    checks,
    ...overrides
  };
}

/** Two problems among five checks in three categories. */
const mixedChecks = (): ConfigHealthCheck[] => [
  check({ id: 'source', category: 'Tool corpora', title: 'GnollHack source', status: 'ok' }),
  check({ id: 'wiki', category: 'Tool corpora', title: 'GnollHack wiki', keys: ['WikiPath'], status: 'warning',
    value: 'D:\\missing\\wiki', summary: 'The directory does not exist.', remedy: 'Clone the wiki and set WikiPath.' }),
  check({ id: 'sql', category: 'Database and email', title: 'SQL connection string',
    keys: ['ConnectionStrings:SqlDatabaseConnection'], status: 'ok', source: 'User Secrets', value: null,
    summary: 'Set.', remedy: 'Add it to User Secrets.' }),
  check({ id: 'email', category: 'Database and email', title: 'Email connection string',
    keys: ['ConnectionStrings:EmailConnection'], status: 'error', source: 'Not set', value: null,
    summary: 'Not set.', remedy: 'Add it to User Secrets.' }),
  check({ id: 'branch', category: 'GnollHack source build', title: 'makedefs branch', keys: ['MakedefsBranch'],
    status: 'info', source: 'Not set', value: null, summary: 'No branch restriction.', remedy: '' })
];

describe('ConfigHealthComponent', () => {
  let fixture: ComponentFixture<ConfigHealthComponent>;
  let devtoolsService: AdminDevtoolsService;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const tab = (filter: 'problems' | 'all'): HTMLButtonElement =>
    host().querySelector(`#ch-filter-${filter}`) as HTMLButtonElement;
  const articleTitles = (): string[] =>
    Array.from(host().querySelectorAll('article.ch-check .ch-check-title')).map(text);
  const articleFor = (title: string): HTMLElement =>
    Array.from(host().querySelectorAll<HTMLElement>('article.ch-check'))
      .find(a => text(a.querySelector('.ch-check-title')) === title)!;

  const render = async (report: ConfigHealthReport): Promise<void> => {
    vi.mocked(devtoolsService.getConfigHealth).mockReturnValue(of(report));
    fixture = TestBed.createComponent(ConfigHealthComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    try { localStorage.removeItem(CONFIG_HEALTH_FILTER_STORAGE_KEY); } catch { /* storage unavailable */ }
    ({ devtoolsService } = await configureAdminTestBed(ConfigHealthComponent));
  });

  afterEach(() => {
    vi.restoreAllMocks();
    try { localStorage.removeItem(CONFIG_HEALTH_FILTER_STORAGE_KEY); } catch { /* storage unavailable */ }
  });

  it('shows the four summary counts and the environment', async () => {
    await render(buildReport(mixedChecks()));

    const tiles = Array.from(host().querySelectorAll('.gh-health-summary .gh-health-tile'));
    expect(tiles.map(t => t.getAttribute('data-status'))).toEqual(['ok', 'warning', 'error', 'info']);
    expect(tiles.map(t => text(t.querySelector('.ch-tile-count')))).toEqual(['2', '1', '1', '1']);
    expect(tiles.map(t => text(t.querySelector('.gh-health-status')))).toEqual(['OK', 'Warnings', 'Errors', 'Info']);
    expect(tiles.every(t => t.querySelector('.gh-health-status svg[aria-hidden="true"]'))).toBe(true);
    expect(text(host().querySelector('.ch-environment'))).toBe('Environment: Development');
  });

  it('lists the configuration sources with their file state and key count', async () => {
    await render(buildReport(mixedChecks(), {
      sources: [
        { name: 'appsettings.json', kind: 'Json', fileExists: true, keyCount: 40 },
        { name: 'User Secrets', kind: 'Json', fileExists: false, keyCount: 0 },
        { name: 'Environment variables', kind: 'EnvironmentVariables', fileExists: null, keyCount: 80 }
      ]
    }));

    const table = host().querySelector('table.ch-sources-table')!;
    expect(text(table.querySelector('caption'))).toContain('Configuration sources');
    const rows = Array.from(table.querySelectorAll('tbody tr')).map(tr => Array.from(tr.children).map(text));
    expect(rows).toEqual([
      ['appsettings.json', 'Json', 'Found', '40'],
      ['User Secrets', 'Json', 'Missing', '0'],
      ['Environment variables', 'EnvironmentVariables', 'Not a file', '80']
    ]);
    expect(table.querySelector('tbody tr:nth-child(2) .gh-health-status')?.getAttribute('data-status')).toBe('warning');
  });

  it('says User Secrets are not used outside Development', async () => {
    await render(buildReport(mixedChecks(), {
      environmentName: 'Production',
      userSecretsExpected: false,
      sources: [{ name: 'appsettings.json', kind: 'Json', fileExists: true, keyCount: 40 }]
    }));

    const last = host().querySelector('table.ch-sources-table tbody tr:last-child')!;
    expect(text(last.querySelector('th'))).toBe('User Secrets');
    expect(text(last)).toContain('Not used in Production');
  });

  it('groups the checks by category in sections with a heading, in report order', async () => {
    await render(buildReport(mixedChecks()));
    tab('all').click();
    fixture.detectChanges();

    const sections = Array.from(host().querySelectorAll('#ch-checks-panel section.ch-category'));
    expect(sections.map(s => text(s.querySelector('h4')))).toEqual(
      ['Tool corpora', 'Database and email', 'GnollHack source build']);
    expect(Array.from(sections[0].querySelectorAll('article')).map(a => text(a.querySelector('h5'))))
      .toEqual(['GnollHack source', 'GnollHack wiki']);
    const heading = sections[1].querySelector('h4')!;
    expect(sections[1].getAttribute('aria-labelledby')).toBe(heading.id);
  });

  it('opens on Problems when there are problems and shows only warnings and errors', async () => {
    await render(buildReport(mixedChecks()));

    expect(text(tab('problems'))).toBe('Problems (2)');
    expect(text(tab('all'))).toBe('All checks (5)');
    expect(tab('problems').getAttribute('aria-selected')).toBe('true');
    expect(tab('problems').getAttribute('tabindex')).toBe('0');
    expect(tab('all').getAttribute('tabindex')).toBe('-1');
    expect(articleTitles()).toEqual(['GnollHack wiki', 'Email connection string']);
    expect(host().querySelector('#ch-checks-panel')?.getAttribute('aria-labelledby')).toBe('ch-filter-problems');
  });

  it('opens on All checks when there are no problems', async () => {
    await render(buildReport([check({ id: 'a', title: 'A' }), check({ id: 'b', title: 'B', status: 'info' })]));

    expect(tab('all').getAttribute('aria-selected')).toBe('true');
    expect(articleTitles()).toEqual(['A', 'B']);
  });

  it('shows the empty Problems view with the check count', async () => {
    localStorage.setItem(CONFIG_HEALTH_FILTER_STORAGE_KEY, 'problems');
    await render(buildReport([check({ id: 'a' }), check({ id: 'b' }), check({ id: 'c' })]));

    expect(tab('problems').getAttribute('aria-selected')).toBe('true');
    expect(text(host().querySelector('.ch-empty'))).toBe('No problems found in 3 checks.');
    expect(host().querySelectorAll('article.ch-check').length).toBe(0);
  });

  it('switches to All checks on click and remembers the choice', async () => {
    await render(buildReport(mixedChecks()));

    tab('all').click();
    fixture.detectChanges();

    expect(tab('all').getAttribute('aria-selected')).toBe('true');
    expect(articleTitles().length).toBe(5);
    expect(localStorage.getItem(CONFIG_HEALTH_FILTER_STORAGE_KEY)).toBe('all');

    fixture.destroy();
    await render(buildReport(mixedChecks()));
    expect(tab('all').getAttribute('aria-selected')).toBe('true');
  });

  it('moves selection and focus with the arrow keys', async () => {
    await render(buildReport(mixedChecks()));

    tab('problems').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(tab('all').getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tab('all'));

    tab('all').dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(tab('problems').getAttribute('aria-selected')).toBe('true');
  });

  it('falls back to the default and still switches when localStorage throws', async () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });

    await render(buildReport(mixedChecks()));
    expect(getItem).toHaveBeenCalled();
    expect(tab('problems').getAttribute('aria-selected')).toBe('true');

    expect(() => { tab('all').click(); fixture.detectChanges(); }).not.toThrow();
    expect(setItem).toHaveBeenCalled();
    expect(tab('all').getAttribute('aria-selected')).toBe('true');
    expect(articleTitles().length).toBe(5);
  });

  it('ignores a stored value that is not a filter', async () => {
    localStorage.setItem(CONFIG_HEALTH_FILTER_STORAGE_KEY, 'everything');
    await render(buildReport(mixedChecks()));

    expect(tab('problems').getAttribute('aria-selected')).toBe('true');
  });

  it('shows a value, Value hidden for a sensitive key, and nothing when not set', async () => {
    await render(buildReport(mixedChecks()));
    tab('all').click();
    fixture.detectChanges();

    const wiki = articleFor('GnollHack wiki');
    expect(text(wiki.querySelector('code.ch-value'))).toBe('D:\\missing\\wiki');
    expect(Array.from(wiki.querySelectorAll('.ch-keys code')).map(text)).toEqual(['WikiPath']);
    expect(text(wiki.querySelector('.ch-source-chip'))).toBe('appsettings.json');

    const sql = articleFor('SQL connection string');
    expect(text(sql.querySelector('.ch-value-hidden'))).toBe('Value hidden');
    expect(sql.querySelector('code.ch-value')).toBeNull();
    expect(text(sql.querySelector('.ch-source-chip'))).toBe('User Secrets');

    const email = articleFor('Email connection string');
    expect(email.querySelector('.ch-value-hidden')).toBeNull();
    expect(email.querySelector('code.ch-value')).toBeNull();
    expect(email.querySelector('.ch-source-chip.is-unset')).not.toBeNull();
  });

  it('offers How to fix only for a check that is not OK and has a remedy', async () => {
    await render(buildReport(mixedChecks()));
    tab('all').click();
    fixture.detectChanges();

    const wiki = articleFor('GnollHack wiki');
    const details = wiki.querySelector('details.gh-disclosure.ch-remedy') as HTMLDetailsElement;
    expect(details).not.toBeNull();
    expect(details.open).toBe(false);
    expect(text(details.querySelector('summary'))).toBe('How to fix');
    expect(text(details.querySelector('.gh-disclosure-body'))).toBe('Clone the wiki and set WikiPath.');

    expect(articleFor('GnollHack source').querySelector('details')).toBeNull();
    expect(articleFor('SQL connection string').querySelector('details')).toBeNull();
    expect(articleFor('makedefs branch').querySelector('details')).toBeNull();
  });

  it('shows the check time and reloads the report on Re-check', async () => {
    await render(buildReport(mixedChecks()));
    const expected = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
      .format(new Date('2026-10-18T12:34:56Z')).replace(/\s+/g, ' ');
    const status = host().querySelector('.ch-checked[role="status"]');
    expect(text(status)).toBe(`Checked at ${expected}`);

    const fixed = mixedChecks().map(c => ({ ...c, status: 'ok' as const }));
    vi.mocked(devtoolsService.getConfigHealth).mockReturnValue(of(buildReport(fixed)));
    const recheck = Array.from(host().querySelectorAll<HTMLButtonElement>('button.btn-ghost'))
      .find(b => text(b) === 'Re-check')!;
    expect(recheck.querySelector('svg.btn-icon[aria-hidden="true"]')).not.toBeNull();
    recheck.click();
    fixture.detectChanges();

    expect(devtoolsService.getConfigHealth).toHaveBeenCalledTimes(2);
    expect(text(tab('problems'))).toBe('Problems (0)');
    expect(text(host().querySelector('.gh-health-tile[data-status="ok"] .ch-tile-count'))).toBe('5');
  });

  it('shows an inline error when the report cannot be loaded', async () => {
    vi.mocked(devtoolsService.getConfigHealth).mockReturnValue(throwError(() => ({ message: 'boom' })));
    fixture = TestBed.createComponent(ConfigHealthComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(host().querySelector('.gh-field-error[role="alert"]')))
      .toBe('The configuration health report could not be loaded. boom');
    expect(host().querySelector('.gh-health-summary')).toBeNull();
  });
});
