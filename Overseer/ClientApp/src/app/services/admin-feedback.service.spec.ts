import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  ADMIN_FEEDBACK_ENDPOINT,
  ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN,
  AdminFeedbackFacets,
  AdminFeedbackService
} from './admin-feedback.service';

const BASE = ADMIN_FEEDBACK_ENDPOINT;

describe('AdminFeedbackService', () => {
  let service: AdminFeedbackService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminFeedbackService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AdminFeedbackService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the controller route prefix', () => {
    expect(BASE).toBe('/api/admin/feedback');
  });

  it('reads and sets the feedback switch', () => {
    const seeded = { enabled: true, setAtUtc: null, setByUserName: null, seeded: true };
    let read: unknown;
    service.getSwitches().subscribe(value => read = value);
    const get = http.expectOne(`${BASE}/switch`);
    expect(get.request.method).toBe('GET');
    get.flush({ chat: seeded });
    expect(read).toEqual({ chat: seeded });

    service.setSwitch('chat', false).subscribe();
    const post = http.expectOne(`${BASE}/switch/chat`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ enabled: false });
    post.flush({ chat: { enabled: false, setAtUtc: '2026-10-21T12:02:00Z', setByUserName: 'tommi', seeded: false } });
  });

  it('sends every issue criterion, each list comma-joined, the models as modelId', () => {
    service.getIssues({
      startDate: '2026-09-21', endDate: '2026-10-21', statuses: ['new', 'regressed'], categories: ['tool_problem', 'own_mistake'],
      modelIds: ['claude-x', 'gpt-x'], providers: ['Anthropic'], users: ['u-1', 'u-2'], groups: ['3', 'none'],
      sources: ['system', 'unknown'], configs: ['7'], versions: ['1.4.2'], tools: ['wiki_search'], resolution: 'without',
      ids: [4, 9], search: ' crystal ', sort: 'regressions', skip: 25, take: 25
    }).subscribe();

    const req = http.expectOne(r => r.url === `${BASE}/issues`);
    expect(req.request.method).toBe('GET');
    const p = req.request.params;
    expect(p.get('startDate')).toBe('2026-09-21');
    expect(p.get('endDate')).toBe('2026-10-21');
    expect(p.get('status')).toBe('new,regressed');
    expect(p.getAll('status')).toEqual(['new,regressed']);
    expect(p.get('category')).toBe('tool_problem,own_mistake');
    expect(p.get('modelId')).toBe('claude-x,gpt-x');
    expect(p.has('model')).toBe(false);
    expect(p.get('provider')).toBe('Anthropic');
    expect(p.get('user')).toBe('u-1,u-2');
    expect(p.get('group')).toBe('3,none');
    expect(p.get('source')).toBe('system,unknown');
    expect(p.get('config')).toBe('7');
    expect(p.get('version')).toBe('1.4.2');
    expect(p.get('tool')).toBe('wiki_search');
    expect(p.get('resolution')).toBe('without');
    expect(p.get('ids')).toBe('4,9');
    expect(p.get('search')).toBe('crystal');
    expect(p.get('sort')).toBe('regressions');
    expect(p.get('skip')).toBe('25');
    expect(p.get('take')).toBe('25');
    req.flush({ totalCount: 0, rows: [] });
  });

  it('leaves empty criteria and a zero skip out of the issues query', () => {
    service.getIssues({ statuses: [], categories: [], modelIds: [' '], search: '  ', ids: [], skip: 0, take: 1 }).subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/issues`);
    expect(req.request.params.keys()).toEqual(['take']);
    req.flush({ totalCount: 0, rows: [] });
  });

  it('fetches the matching issue ids with the same filters and the sort', () => {
    let result: unknown;
    service.getIssueIds({ statuses: ['new'], categories: ['tool_problem'], search: 'wand' }, 'count').subscribe(r => result = r);
    const req = http.expectOne(r => r.url === `${BASE}/issues/ids`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('status')).toBe('new');
    expect(req.request.params.get('category')).toBe('tool_problem');
    expect(req.request.params.get('search')).toBe('wand');
    expect(req.request.params.get('sort')).toBe('count');
    req.flush({ ids: [3, 1], totalCount: 2, truncated: false });
    expect(result).toEqual({ ids: [3, 1], totalCount: 2, truncated: false });

    service.getIssueIds({ ids: [5, 6] }).subscribe();
    const only = http.expectOne(r => r.url === `${BASE}/issues/ids`);
    expect(only.request.params.keys()).toEqual(['ids']);
    expect(only.request.params.get('ids')).toBe('5,6');
    only.flush({ ids: [5], totalCount: 1, truncated: false });
  });

  it('fetches the facets of a date range, and the counts', () => {
    const facets: AdminFeedbackFacets = {
      categories: [{ value: 'tool_problem', count: 2 }], models: [], providers: [], sources: [], versions: [], tools: [],
      users: [{ id: 'u-1', name: 'alice', count: 1 }], groups: [{ id: 'none', name: 'No group', count: 1 }], configs: []
    };
    let read: AdminFeedbackFacets | undefined;
    service.getFacets('2026-09-21', '2026-10-21').subscribe(value => read = value);
    const req = http.expectOne(r => r.url === `${BASE}/facets`);
    expect(req.request.params.get('startDate')).toBe('2026-09-21');
    expect(req.request.params.get('endDate')).toBe('2026-10-21');
    req.flush(facets);
    expect(read).toEqual(facets);

    service.getFacets().subscribe();
    const all = http.expectOne(r => r.url === `${BASE}/facets`);
    expect(all.request.params.keys()).toEqual([]);
    all.flush(facets);

    service.getCounts().subscribe();
    http.expectOne(`${BASE}/counts`).flush({ new: 3, acknowledged: 0, resolved: 1, regressed: 1, dismissed: 0 });
  });

  it('has no models endpoint any more', () => {
    expect((service as unknown as Record<string, unknown>)['getModels']).toBeUndefined();
  });

  it('pages an issue\'s occurrences, with the occurrence filters, or all of them', () => {
    service.getOccurrences(12, 10, 10).subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/issues/12/occurrences`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('skip')).toBe('10');
    expect(req.request.params.get('take')).toBe('10');
    expect(req.request.params.has('all')).toBe(false);
    req.flush({ totalCount: 0, rows: [] });

    service.getOccurrences(12, 0, 10, { modelIds: ['gpt-x'], sources: ['user'] }).subscribe();
    const filtered = http.expectOne(r => r.url === `${BASE}/issues/12/occurrences`);
    expect(filtered.request.params.has('skip')).toBe(false);
    expect(filtered.request.params.get('modelId')).toBe('gpt-x');
    expect(filtered.request.params.get('source')).toBe('user');
    filtered.flush({ totalCount: 0, rows: [] });

    service.getOccurrences(12, 0, 10, { modelIds: ['gpt-x'] }, true).subscribe();
    const all = http.expectOne(r => r.url === `${BASE}/issues/12/occurrences`);
    expect(all.request.params.get('all')).toBe('true');
    all.flush({ totalCount: 0, rows: [] });
  });

  it('posts a status alone when nothing else changes, so the stored fields are kept, and returns the updated issue', () => {
    let returnedStatus = '';
    service.updateStatus(7, 'resolved').subscribe(issue => returnedStatus = issue.status);
    const req = http.expectOne(`${BASE}/issues/7/status`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ status: 'resolved' });
    req.flush({ id: 7, status: 'resolved' });
    expect(returnedStatus).toBe('resolved');
  });

  it('posts each given field trimmed, an empty one to clear it, and leaves out a null one', () => {
    service.updateStatus(7, 'resolved', { note: '  Fixed in 1.4  ', resolution: ' Added the wiki page. ', fixedInVersion: ' 1.4.1 ' }).subscribe();
    const full = http.expectOne(`${BASE}/issues/7/status`);
    expect(full.request.body).toEqual({ status: 'resolved', note: 'Fixed in 1.4', resolution: 'Added the wiki page.', fixedInVersion: '1.4.1' });
    full.flush(null);

    service.updateStatus(7, 'dismissed', { note: '   ', resolution: null, fixedInVersion: '' }).subscribe();
    const cleared = http.expectOne(`${BASE}/issues/7/status`);
    expect(cleared.request.body).toEqual({ status: 'dismissed', note: '', fixedInVersion: '' });
    cleared.flush(null);
  });

  it('checks a fixed-in version as the server does', () => {
    expect(ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN.test('1.4.1')).toBe(true);
    expect(ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN.test('1.4.2+abc-rc1')).toBe(true);
    expect(ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN.test('1.4 beta')).toBe(false);
    expect(ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN.test('')).toBe(false);
    expect(ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN.test('v'.repeat(65))).toBe(false);
  });

  it('deletes an issue, which answers 204 with no body', () => {
    let done = false;
    service.deleteIssue(7).subscribe({ complete: () => done = true });
    const req = http.expectOne(`${BASE}/issues/7`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
  });
});
