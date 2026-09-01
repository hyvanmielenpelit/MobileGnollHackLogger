import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { AdminBenchmarkService } from './admin-benchmark.service';

describe('AdminBenchmarkService', () => {
  let service: AdminBenchmarkService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AdminBenchmarkService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(AdminBenchmarkService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should get suites', () => {
    const mockSuites = [{ id: 1, name: 'Default Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 15 }];
    service.getSuites().subscribe(res => {
      expect(res.length).toBe(1);
      expect(res[0].name).toBe('Default Suite');
    });

    const req = httpMock.expectOne('/api/admin/benchmark/suites');
    expect(req.request.method).toBe('GET');
    req.flush(mockSuites);
  });

  it('should create suite', () => {
    service.createSuite({ name: 'New Suite', description: 'Desc' }).subscribe(res => {
      expect(res.name).toBe('New Suite');
    });

    const req = httpMock.expectOne('/api/admin/benchmark/suites');
    expect(req.request.method).toBe('POST');
    req.flush({ id: 2, name: 'New Suite', description: 'Desc', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 0 });
  });

  it('should start a run', () => {
    service.startRun({ suiteId: 1, testedModelConfigurationId: 10, assessorModelConfigurationId: 20 }).subscribe(res => {
      expect(res.runId).toBe(42);
    });

    const req = httpMock.expectOne('/api/admin/benchmark/runs');
    expect(req.request.method).toBe('POST');
    req.flush({ runId: 42 });
  });
});
