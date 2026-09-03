import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SnapshotViewerComponent } from './snapshot-viewer.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { of } from 'rxjs';

describe('SnapshotViewerComponent', () => {
  let component: SnapshotViewerComponent;
  let fixture: ComponentFixture<SnapshotViewerComponent>;
  let mockBenchmarkService: jasmine.SpyObj<AdminBenchmarkService>;

  beforeEach(async () => {
    mockBenchmarkService = jasmine.createSpyObj('AdminBenchmarkService', [
      'getSnapshot',
      'getSnapshotTextUrl',
      'updateSnapshot'
    ]);

    mockBenchmarkService.getSnapshot.and.returnValue(of({
      id: 1,
      name: 'Emergency Low HP',
      charCount: 15000,
      sha256: 'abc1234567890',
      captureMethod: 'client_refresh_snapshot',
      sanitizedText: 'Line 1\nLine 2',
      createdAtUtc: new Date().toISOString()
    }));

    await TestBed.configureTestingModule({
      imports: [SnapshotViewerComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: mockBenchmarkService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SnapshotViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load snapshot when open is called', () => {
    component.open(1);
    expect(mockBenchmarkService.getSnapshot).toHaveBeenCalledWith(1, true);
    expect(component.snapshot?.name).toBe('Emergency Low HP');
  });

  it('should detect truncation marker', () => {
    component.snapshot = {
      id: 1,
      name: 'Test',
      charCount: 100,
      sha256: '123',
      captureMethod: 'test',
      sanitizedText: 'Some text [SNAPSHOT TRUNCATED at 60000 chars]',
      createdAtUtc: new Date().toISOString()
    };
    expect(component.hasTruncationMarker).toBeTrue();
  });
});
