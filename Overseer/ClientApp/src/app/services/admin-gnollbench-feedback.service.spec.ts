import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT,
  AdminGnollBenchFeedbackService,
  gnollBenchFilterParams
} from './admin-gnollbench-feedback.service';

const BASE = ADMIN_GNOLLBENCH_FEEDBACK_ENDPOINT;
const FP = 'a'.repeat(64);

describe('AdminGnollBenchFeedbackService', () => {
  let service: AdminGnollBenchFeedbackService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminGnollBenchFeedbackService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AdminGnollBenchFeedbackService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the controller route prefix', () => {
    expect(BASE).toBe('/api/admin/feedback/gnollbench');
  });

  it('sends every filter under its server name, each list comma-joined', () => {
    service.getIssues({
      startDate: '2026-09-21', endDate: '2026-10-21', statuses: ['new', 'regressed'], accepted: 'all',
      categories: ['tool_problem'], models: ['claude-x', 'gpt-x'], providers: ['Anthropic'], runs: ['12', '13'],
      series: ['4'], groups: ['5'], batteries: ['6'], batches: ['7'], suites: ['8'], questions: ['9', '10'],
      conditions: ['sourceOnly'], corpora: ['live', 'scarce-1'], variants: ['none', 'asker-profile-v1/minus/guide.wiki_search'],
      harnesses: ['60'], fingerprints: [FP], search: ' crystal ', sort: 'runs', skip: 25, take: 25
    }).subscribe();

    const req = http.expectOne(r => r.url === `${BASE}/issues`);
    expect(req.request.method).toBe('GET');
    const p = req.request.params;
    expect(p.get('startDate')).toBe('2026-09-21');
    expect(p.get('endDate')).toBe('2026-10-21');
    expect(p.get('status')).toBe('new,regressed');
    expect(p.getAll('status')).toEqual(['new,regressed']);
    expect(p.get('accepted')).toBe('all');
    expect(p.get('category')).toBe('tool_problem');
    expect(p.get('model')).toBe('claude-x,gpt-x');
    expect(p.get('provider')).toBe('Anthropic');
    expect(p.get('run')).toBe('12,13');
    expect(p.get('series')).toBe('4');
    expect(p.get('group')).toBe('5');
    expect(p.get('battery')).toBe('6');
    expect(p.get('batch')).toBe('7');
    expect(p.get('suite')).toBe('8');
    expect(p.get('question')).toBe('9,10');
    expect(p.get('condition')).toBe('sourceOnly');
    expect(p.get('corpus')).toBe('live,scarce-1');
    expect(p.get('variant')).toBe('none,asker-profile-v1/minus/guide.wiki_search');
    expect(p.get('harness')).toBe('60');
    expect(p.get('fingerprints')).toBe(FP);
    expect(p.get('search')).toBe('crystal');
    expect(p.get('sort')).toBe('runs');
    expect(p.get('skip')).toBe('25');
    expect(p.get('take')).toBe('25');
    req.flush({ totalCount: 0, rows: [] });
  });

  it('leaves empty filters, the accepted default and a zero skip out of the issues query', () => {
    service.getIssues({ statuses: [], categories: [' '], search: '  ', accepted: 'accepted', skip: 0, take: 1 }).subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/issues`);
    expect(req.request.params.keys()).toEqual(['take']);
    req.flush({ totalCount: 0, rows: [] });
  });

  it('fetches the matching fingerprints with the same filters and no paging', () => {
    let ids: unknown;
    service.getIssueIds({ runs: ['12'], fingerprints: [FP], sort: 'count' }).subscribe(value => ids = value);
    const req = http.expectOne(r => r.url === `${BASE}/issues/ids`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('run')).toBe('12');
    expect(req.request.params.get('fingerprints')).toBe(FP);
    expect(req.request.params.has('skip')).toBe(false);
    expect(req.request.params.has('take')).toBe(false);
    req.flush({ fingerprints: [FP], totalCount: 1, truncated: false });
    expect(ids).toEqual({ fingerprints: [FP], totalCount: 1, truncated: false });
  });

  it('pages an issue\'s self-reports with the occurrence-level filters, or all of them', () => {
    service.getOccurrences(FP, {
      filters: { startDate: '2026-09-21', statuses: ['new'], sort: 'count', runs: ['12'], accepted: 'refused' },
      skip: 10, take: 10
    }).subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/issues/${FP}/occurrences`);
    expect(req.request.method).toBe('GET');
    const p = req.request.params;
    expect(p.get('run')).toBe('12');
    expect(p.get('accepted')).toBe('refused');
    expect(p.get('skip')).toBe('10');
    expect(p.get('take')).toBe('10');
    expect(p.has('startDate')).toBe(false);
    expect(p.has('status')).toBe(false);
    expect(p.has('sort')).toBe(false);
    expect(p.has('all')).toBe(false);
    req.flush({ totalCount: 0, rows: [] });

    service.getOccurrences(FP, { all: true, take: 10 }).subscribe();
    const all = http.expectOne(r => r.url === `${BASE}/issues/${FP}/occurrences`);
    expect(all.request.params.get('all')).toBe('true');
    expect(all.request.params.has('skip')).toBe(false);
    all.flush({ totalCount: 0, rows: [] });
  });

  it('fetches the facets for a range and a base set, and the counts', () => {
    service.getFacets({ startDate: '2026-09-21', endDate: '2026-10-21', accepted: 'refused' }).subscribe();
    const facets = http.expectOne(r => r.url === `${BASE}/facets`);
    expect(facets.request.params.get('startDate')).toBe('2026-09-21');
    expect(facets.request.params.get('endDate')).toBe('2026-10-21');
    expect(facets.request.params.get('accepted')).toBe('refused');
    facets.flush({});

    service.getFacets().subscribe();
    const unbounded = http.expectOne(r => r.url === `${BASE}/facets`);
    expect(unbounded.request.params.keys()).toEqual([]);
    unbounded.flush({});

    service.getCounts().subscribe();
    http.expectOne(`${BASE}/counts`).flush({ new: 3, acknowledged: 0, resolved: 1, regressed: 1, dismissed: 0 });
  });

  it('posts only the triage fields given: absent keeps, empty clears', () => {
    let status = '';
    service.updateStatus(FP, { status: 'resolved', resolution: 'Wiki page added.', fixedInVersion: '' })
      .subscribe(issue => status = issue.status);
    const req = http.expectOne(`${BASE}/issues/${FP}/status`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ status: 'resolved', resolution: 'Wiki page added.', fixedInVersion: '' });
    req.flush({ fingerprint: FP, status: 'resolved' });
    expect(status).toBe('resolved');

    service.updateStatus(FP, { status: 'dismissed', note: '', resolution: null }).subscribe();
    const cleared = http.expectOne(`${BASE}/issues/${FP}/status`);
    expect(cleared.request.body).toEqual({ status: 'dismissed', note: '' });
    cleared.flush(null);
  });

  it('escapes the fingerprint in the path', () => {
    service.getOccurrences('a/b').subscribe();
    http.expectOne(r => r.url === `${BASE}/issues/a%2Fb/occurrences`).flush({ totalCount: 0, rows: [] });
  });

  it('builds the filter parameters without a request', () => {
    expect(gnollBenchFilterParams({ variants: ['none'], accepted: 'accepted' }).keys()).toEqual(['variant']);
  });
});
