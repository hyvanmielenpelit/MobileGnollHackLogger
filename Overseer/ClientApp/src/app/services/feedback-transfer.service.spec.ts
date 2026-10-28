import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  FEEDBACK_RESOLVED_VIA_IDS,
  FEEDBACK_TRANSFER_ENDPOINT,
  FeedbackResolutionRequest,
  FeedbackTransferService,
  feedbackExportNotFound,
  feedbackResolutionConflict,
  feedbackResolutionInvalid,
  feedbackTransferStatus
} from './feedback-transfer.service';

const FP = 'a'.repeat(64);

describe('FeedbackTransferService', () => {
  let service: FeedbackTransferService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [FeedbackTransferService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(FeedbackTransferService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the controller route prefix', () => {
    expect(FEEDBACK_TRANSFER_ENDPOINT).toBe('/api/admin/feedback');
  });

  it('lists the resolved-via ids in the canonical order', () => {
    expect([...FEEDBACK_RESOLVED_VIA_IDS]).toEqual(
      ['wiki', 'knowledge_base', 'overseer_release', 'gnollhack_release', 'server_configuration', 'other']);
  });

  it('posts an export with its format and returns the file, its content and its media type', () => {
    let result: { fileName: string; content: string; mediaType: string } | null = null;
    service.exportIssues({ source: 'overseer', format: 'yaml', issueIds: [42, 7], occurrencesPerIssue: 25 })
      .subscribe(r => result = { fileName: r.fileName, content: r.content, mediaType: r.mediaType });

    const req = http.expectOne(`${FEEDBACK_TRANSFER_ENDPOINT}/export`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ source: 'overseer', format: 'yaml', issueIds: [42, 7], occurrencesPerIssue: 25 });
    req.flush({
      fileName: 'overseer-feedback-overseer-20261023-1234.yaml', content: 'format: overseer-feedback-issues\n',
      mediaType: 'application/yaml;charset=utf-8', issueCount: 2, missingIds: [], missingFingerprints: []
    });
    expect(result).toEqual({
      fileName: 'overseer-feedback-overseer-20261023-1234.yaml', content: 'format: overseer-feedback-issues\n',
      mediaType: 'application/yaml;charset=utf-8'
    });
  });

  it('gets the format examples of a source', () => {
    let issuesName = '';
    let resolutionsType = '';
    service.formatExamples('gnollbench').subscribe(r => {
      issuesName = r.issues.fileName;
      resolutionsType = r.resolutions.mediaType;
    });

    const req = http.expectOne(r => r.url === `${FEEDBACK_TRANSFER_ENDPOINT}/format-examples`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('source')).toBe('gnollbench');
    expect(req.request.urlWithParams).toBe(`${FEEDBACK_TRANSFER_ENDPOINT}/format-examples?source=gnollbench`);
    req.flush({
      issues: { fileName: 'example-overseer-feedback-gnollbench-20261028-0915.yaml', content: 'format: x\n', mediaType: 'application/yaml;charset=utf-8' },
      resolutions: { fileName: 'example-agent-feedback-resolutions-gnollbench_v1.yaml', content: 'format: y\n', mediaType: 'application/yaml;charset=utf-8' }
    });
    expect(issuesName).toBe('example-overseer-feedback-gnollbench-20261028-0915.yaml');
    expect(resolutionsType).toBe('application/yaml;charset=utf-8');
  });

  it('posts a preview and an apply with the same body shape', () => {
    const body: FeedbackResolutionRequest = {
      format: 'overseer-feedback-resolutions', version: 2, source: 'gnollbench', exportedAtUtc: '2026-10-23T12:34:56Z',
      items: [{ fingerprint: FP, status: 'resolved', resolution: 'Fixed.', resolvedVia: ['wiki', 'knowledge_base'] }]
    };
    service.previewResolutions(body).subscribe();
    const preview = http.expectOne(`${FEEDBACK_TRANSFER_ENDPOINT}/resolutions/preview`);
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual(body);
    preview.flush({ source: 'gnollbench', exportedAtUtc: null, errorCount: 0, changeCount: 1, triagedSinceExportCount: 0, notices: [], items: [] });

    let changed = -1;
    const apply = { ...body, items: [{ ...body.items[0], expectedStatus: 'new' as const, expectedTriagedUtc: null }] };
    service.applyResolutions(apply).subscribe(r => changed = r.changed);
    const req = http.expectOne(`${FEEDBACK_TRANSFER_ENDPOINT}/resolutions/apply`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.items[0]).toEqual({
      fingerprint: FP, status: 'resolved', resolution: 'Fixed.', resolvedVia: ['wiki', 'knowledge_base'], expectedStatus: 'new', expectedTriagedUtc: null
    });
    req.flush({ changed: 1, unchanged: 0 });
    expect(changed).toBe(1);
  });

  describe('error bodies', () => {
    const error = (status: number, body: unknown): HttpErrorResponse => new HttpErrorResponse({ status, error: body });

    it('reads the status', () => {
      expect(feedbackTransferStatus(error(409, null))).toBe(409);
      expect(feedbackTransferStatus(new Error('x'))).toBe(0);
      expect(feedbackTransferStatus(null)).toBe(0);
    });

    it('reads a refused file', () => {
      const invalid = feedbackResolutionInvalid(error(400, {
        message: 'The file has 2 errors.',
        errors: [{ index: null, message: 'format must be `overseer-feedback-resolutions`.' }, { index: 3, message: 'issues[3] (id 42): unknown key `state`.' }]
      }));
      expect(invalid).toEqual({
        message: 'The file has 2 errors.',
        errors: [{ index: null, message: 'format must be `overseer-feedback-resolutions`.' }, { index: 3, message: 'issues[3] (id 42): unknown key `state`.' }]
      });
      expect(feedbackResolutionInvalid(error(500, { message: 'x' }))).toBeNull();
      expect(feedbackResolutionInvalid(error(400, 'plain text'))).toBeNull();
    });

    it('reads a conflict', () => {
      const conflict = feedbackResolutionConflict(error(409, {
        message: 'One issue changed after the preview, so nothing was applied. Review the file again.',
        conflicts: [{ index: 0, id: 42, fingerprint: FP, reason: 'changed', expectedStatus: 'new', currentStatus: 'resolved', expectedTriagedUtc: null, currentTriagedUtc: '2026-10-23T13:00:00Z' }]
      }));
      expect(conflict?.conflicts.length).toBe(1);
      expect(conflict?.conflicts[0].currentStatus).toBe('resolved');
      expect(feedbackResolutionConflict(error(400, { message: 'x' }))).toBeNull();
    });

    it('reads an export of issues that are all gone', () => {
      const missing = feedbackExportNotFound(error(404, { message: 'None of the issues exist.', missingIds: [42], missingFingerprints: [] }));
      expect(missing).toEqual({ message: 'None of the issues exist.', missingIds: [42], missingFingerprints: [] });
      expect(feedbackExportNotFound(error(400, { message: 'x' }))).toBeNull();
    });
  });
});
