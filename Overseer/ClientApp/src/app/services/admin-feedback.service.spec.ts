import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ADMIN_FEEDBACK_ENDPOINT, AdminFeedbackService } from './admin-feedback.service';

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

  it('sends every issue criterion, the statuses comma-joined', () => {
    service.getIssues({
      startDate: '2026-09-21', endDate: '2026-10-21', statuses: ['new', 'regressed'], category: 'tool_problem',
      modelId: 'claude-x', search: ' crystal ', sort: 'count', skip: 25, take: 25
    }).subscribe();

    const req = http.expectOne(r => r.url === `${BASE}/issues`);
    expect(req.request.method).toBe('GET');
    const p = req.request.params;
    expect(p.get('startDate')).toBe('2026-09-21');
    expect(p.get('endDate')).toBe('2026-10-21');
    expect(p.get('status')).toBe('new,regressed');
    expect(p.getAll('status')).toEqual(['new,regressed']);
    expect(p.get('category')).toBe('tool_problem');
    expect(p.get('modelId')).toBe('claude-x');
    expect(p.get('search')).toBe('crystal');
    expect(p.get('sort')).toBe('count');
    expect(p.get('skip')).toBe('25');
    expect(p.get('take')).toBe('25');
    req.flush({ totalCount: 0, rows: [] });
  });

  it('leaves empty criteria and a zero skip out of the issues query', () => {
    service.getIssues({ statuses: [], search: '  ', category: '', skip: 0, take: 1 }).subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/issues`);
    expect(req.request.params.keys()).toEqual(['take']);
    req.flush({ totalCount: 0, rows: [] });
  });

  it('fetches the models and the counts', () => {
    service.getModels().subscribe();
    http.expectOne(`${BASE}/models`).flush(['claude-x']);

    service.getCounts().subscribe();
    http.expectOne(`${BASE}/counts`).flush({ new: 3, acknowledged: 0, resolved: 1, regressed: 1, dismissed: 0 });
  });

  it('pages an issue\'s occurrences', () => {
    service.getOccurrences(12, 10, 10).subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/issues/12/occurrences`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('skip')).toBe('10');
    expect(req.request.params.get('take')).toBe('10');
    req.flush({ totalCount: 0, rows: [] });

    service.getOccurrences(12, 0, 10).subscribe();
    const first = http.expectOne(r => r.url === `${BASE}/issues/12/occurrences`);
    expect(first.request.params.has('skip')).toBe(false);
    first.flush({ totalCount: 0, rows: [] });
  });

  it('posts a status with a trimmed note, a blank note as null, and returns the updated issue', () => {
    let returnedStatus = '';
    service.updateStatus(7, 'resolved', '  Fixed in 1.4  ').subscribe(issue => returnedStatus = issue.status);
    const req = http.expectOne(`${BASE}/issues/7/status`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ status: 'resolved', note: 'Fixed in 1.4' });
    req.flush({ id: 7, status: 'resolved' });
    expect(returnedStatus).toBe('resolved');

    service.updateStatus(7, 'dismissed', '   ').subscribe();
    const blank = http.expectOne(`${BASE}/issues/7/status`);
    expect(blank.request.body).toEqual({ status: 'dismissed', note: null });
    blank.flush(null);
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
