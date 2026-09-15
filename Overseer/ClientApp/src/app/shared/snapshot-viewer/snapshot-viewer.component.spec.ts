import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
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

  describe('the toolbar', () => {
    beforeEach(() => {
      component.open(1);
      fixture.detectChanges();
    });

    afterEach(() => component.viewerDialog?.nativeElement?.close());

    function buttonNamed(name: string): HTMLButtonElement | undefined {
      const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
      return buttons.find(b => (b.getAttribute('aria-label') ?? b.textContent ?? '').trim() === name);
    }

    function liveText(selector: string): string {
      return ((fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent ?? '').trim();
    }

    it('names every action in words, with no emoji', () => {
      for (const name of ['Edit Metadata', 'Copy Text', 'Download .snapshot.txt']) {
        const button = buttonNamed(name);
        expect(button).withContext(name).toBeTruthy();
        expect(/\p{Extended_Pictographic}/u.test(button?.textContent ?? '')).withContext(name).toBeFalse();
      }
      expect(buttonNamed('Copy SHA-256')).toBeTruthy();
      expect(buttonNamed('Close snapshot board')).toBeTruthy();
    });

    it('announces a text copy, then clears the announcement', fakeAsync(() => {
      spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());

      component.copyText();
      flushMicrotasks();
      fixture.detectChanges();
      expect(liveText('.copy-text-status')).toBe('Copied');
      expect(buttonNamed('Copied')).toBeTruthy();

      tick(2000);
      fixture.detectChanges();
      expect(liveText('.copy-text-status')).toBe('');
      expect(buttonNamed('Copy Text')).toBeTruthy();
    }));

    it('announces a SHA-256 copy', fakeAsync(() => {
      spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());

      component.copySha();
      flushMicrotasks();
      fixture.detectChanges();
      expect(liveText('.copy-sha-status')).toBe('Copied');

      tick(2000);
      fixture.detectChanges();
      expect(liveText('.copy-sha-status')).toBe('');
    }));
  });
});
