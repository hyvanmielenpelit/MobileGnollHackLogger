import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AttachedSnapshotService, SaveAttachedSnapshotRequest } from './attached-snapshot.service';

describe('AttachedSnapshotService', () => {
  let service: AttachedSnapshotService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(AttachedSnapshotService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('reads a session\'s attached snapshot info', () => {
    service.getAttachedSnapshotInfo('42').subscribe();

    const req = httpMock.expectOne('/api/admin/benchmark/snapshots/attached/42');
    expect(req.request.method).toBe('GET');
    req.flush({ sessionId: 42, hasSnapshot: true, charCount: 10, existingBoards: [] });
  });

  it('saves the attached snapshot from the session', () => {
    const body: SaveAttachedSnapshotRequest = { sessionId: '42', name: 'Board', notes: 'n', sourceGnollHackVersion: '0.9.4' };
    service.saveAttachedSnapshot(body).subscribe();

    const req = httpMock.expectOne('/api/admin/benchmark/snapshots/from-session');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });
});
