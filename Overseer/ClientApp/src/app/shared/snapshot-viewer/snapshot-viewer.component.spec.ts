import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { SnapshotViewerComponent } from './snapshot-viewer.component';
import { AdminBenchmarkService, BenchmarkGameSnapshotDto } from '../../services/admin-benchmark.service';
import { of } from 'rxjs';

/* A board shaped like a real snapshot: prose, a legend line naming the hero, the map block the
   way dump_map_ai() writes it, then filler to 250 lines. The hero '@' is at <10,13>. */
function buildBoard(): string {
  const lines = ['Map:', 'The hero is at <10,13>, shown as \'@\'.', 'A food ration lies here.', 'Map grid:'];
  let tens = '    ';
  let units = '    ';
  for (let x = 1; x < 80; x++) {
    tens += x % 10 === 0 ? String(x / 10) : ' ';
    units += String(x % 10);
  }
  lines.push(tens.trimEnd(), units);
  for (let y = 0; y <= 20; y++) {
    const gutter = (y < 10 ? ' ' : '') + y + ': ';
    const cells = y === 13 ? '---------@....%....|' : '  |....|';
    lines.push((gutter + cells).trimEnd());
  }
  lines.push('', 'Inventory:', 'a - 2 food rations');
  while (lines.length < 250) lines.push(`filler ${lines.length + 1}`);
  return lines.join('\n');
}

function snapshotWith(text: string, extra: Partial<BenchmarkGameSnapshotDto> = {}): BenchmarkGameSnapshotDto {
  return {
    id: 1,
    name: 'Emergency Low HP',
    charCount: text.length,
    sha256: 'abc1234567890',
    captureMethod: 'client_refresh_snapshot',
    sanitizedText: text,
    createdAtUtc: new Date().toISOString(),
    ...extra
  } as BenchmarkGameSnapshotDto;
}

describe('SnapshotViewerComponent', () => {
  let component: SnapshotViewerComponent;
  let fixture: ComponentFixture<SnapshotViewerComponent>;
  let mockBenchmarkService: jasmine.SpyObj<AdminBenchmarkService>;
  let getItemSpy: jasmine.Spy;

  beforeEach(async () => {
    getItemSpy = spyOn(Storage.prototype, 'getItem').and.returnValue(null);

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
      for (const name of ['Edit Metadata', 'Copy Text', 'Copy with line numbers', 'Download .snapshot.txt']) {
        const button = buttonNamed(name);
        expect(button).withContext(name).toBeTruthy();
      }
      for (const name of ['Copy SHA-256', 'Close snapshot board', 'Go to line', 'Previous match in board', 'Next match in board']) {
        expect(buttonNamed(name)).withContext(name).toBeTruthy();
      }
      const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
      for (const button of buttons) {
        expect(/\p{Extended_Pictographic}/u.test(button.textContent ?? '')).withContext(button.textContent ?? '').toBeFalse();
      }
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

  describe('the reader', () => {
    let host: HTMLElement;

    function openWith(snapshot: BenchmarkGameSnapshotDto) {
      mockBenchmarkService.getSnapshot.and.returnValue(of(snapshot));
      component.open(1);
      fixture.detectChanges();
      host = fixture.nativeElement as HTMLElement;
    }

    function line(n: number): HTMLElement {
      return host.querySelector<HTMLElement>(`.reader-line[data-ln="${n}"]`)!;
    }

    function heroRowCenter(): { row: HTMLElement; text: Text; x: number; y: number } {
      const rowIndex = component.mapBlock!.rowByY.get(13)!;
      const row = line(rowIndex + 1);
      const text = row.firstChild as Text;
      const range = document.createRange();
      range.setStart(text, 13);
      range.setEnd(text, 14);
      const box = range.getBoundingClientRect();
      return { row, text, x: box.left + box.width / 2, y: box.top + box.height / 2 };
    }

    afterEach(() => component.viewerDialog?.nativeElement?.close());

    it('renders a 250-line board as three numbered chunks', () => {
      openWith(snapshotWith(Array.from({ length: 250 }, (_, i) => `row ${i + 1}`).join('\n')));
      const chunks = host.querySelectorAll('.reader-chunk');
      expect(chunks.length).toBe(3);
      const lines = host.querySelectorAll<HTMLElement>('.reader-line');
      expect(lines.length).toBe(250);
      expect(lines[0].dataset['ln']).toBe('1');
      expect(lines[249].dataset['ln']).toBe('250');
      expect(lines[0].textContent).toBe('row 1');
    });

    it('is a focusable, named region', () => {
      openWith(snapshotWith(buildBoard()));
      const region = host.querySelector<HTMLElement>('.reader-scroll')!;
      expect(region.getAttribute('role')).toBe('region');
      expect(region.getAttribute('aria-label')).toBe('Board text of Emergency Low HP');
      expect(region.getAttribute('tabindex')).toBe('0');
    });

    it('toggles line numbers and wrapping, and remembers both', fakeAsync(() => {
      const setItem = spyOn(Storage.prototype, 'setItem');
      openWith(snapshotWith(buildBoard()));
      /* ngModel writes the checkboxes' initial checked state in a microtask. */
      flushMicrotasks();
      fixture.detectChanges();
      const region = host.querySelector<HTMLElement>('.reader-scroll')!;
      expect(region.classList.contains('no-ln')).toBeFalse();
      expect(region.classList.contains('wrap')).toBeFalse();

      host.querySelector<HTMLInputElement>('.line-numbers-toggle')!.click();
      host.querySelector<HTMLInputElement>('.wrap-toggle')!.click();
      fixture.detectChanges();

      expect(region.classList.contains('no-ln')).toBeTrue();
      expect(region.classList.contains('wrap')).toBeTrue();
      expect(setItem).toHaveBeenCalledWith('overseer.snapshotReader.lineNumbers', '0');
      expect(setItem).toHaveBeenCalledWith('overseer.snapshotReader.wrap', '1');
    }));

    it('falls back to the defaults when storage throws', () => {
      getItemSpy.and.throwError(new Error('storage denied'));
      const second = TestBed.createComponent(SnapshotViewerComponent);
      second.detectChanges();
      expect(second.componentInstance.showLineNumbers).toBeTrue();
      expect(second.componentInstance.wrapLines).toBeFalse();
      second.destroy();
    });

    it('keeps the digest in a closed disclosure, and omits it when absent', () => {
      openWith(snapshotWith(buildBoard(), { digestText: 'Hero at low HP beside a fountain.' }));
      const digest = host.querySelector<HTMLDetailsElement>('details.digest-disclosure');
      expect(digest).toBeTruthy();
      expect(digest!.open).toBeFalse();

      component.viewerDialog.nativeElement.close();
      openWith(snapshotWith(buildBoard()));
      expect(host.querySelector('details.digest-disclosure')).toBeNull();
    });

    it('lists the map grid among the sections', () => {
      openWith(snapshotWith(buildBoard()));
      const options = Array.from(host.querySelectorAll<HTMLOptionElement>('.section-select option')).map(o => o.textContent ?? '');
      expect(options.some(o => o.startsWith('Map grid:'))).toBeTrue();
      expect(options.some(o => o.startsWith('Inventory:'))).toBeTrue();
    });

    it('marks the target line after going to it', () => {
      openWith(snapshotWith(buildBoard()));
      component.goToLine(200);
      expect(line(200).classList.contains('is-target')).toBeTrue();
      expect(host.querySelectorAll('.is-target').length).toBe(1);
    });

    it('finds text, reports the count, and steps between matches', fakeAsync(() => {
      openWith(snapshotWith(buildBoard()));
      const input = host.querySelector<HTMLInputElement>('.find-input')!;
      input.value = 'ration';
      input.dispatchEvent(new Event('input'));
      tick(150);
      fixture.detectChanges();

      expect(host.querySelector('.find-count')!.textContent!.trim()).toBe('1 of 2');
      expect(component.targetLine).toBe(3);
      if ('highlights' in CSS) {
        expect(CSS.highlights.has('reader-find')).toBeTrue();
      }

      component.stepMatch(1);
      fixture.detectChanges();
      expect(host.querySelector('.find-count')!.textContent!.trim()).toBe('2 of 2');
      expect(component.targetLine).toBe(30);

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', cancelable: true }));
      fixture.detectChanges();
      expect(host.querySelector('.find-count')!.textContent!.trim()).toBe('');
      tick(2000);
    }));

    it('reads the map cell under the pointer as <x,y> and its symbol', () => {
      openWith(snapshotWith(buildBoard()));
      const { text, x, y } = heroRowCenter();
      spyOn(document, 'caretPositionFromPoint').and.returnValue({ offsetNode: text, offset: 13 } as unknown as CaretPosition);

      component.updateCellReadout(x, y);
      expect(component.cellReadout).toBe('<10,13>  \'@\'');
    });

    it('copies the coordinate of a clicked map cell', () => {
      openWith(snapshotWith(buildBoard()));
      const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      const { row, text, x, y } = heroRowCenter();
      spyOn(document, 'caretPositionFromPoint').and.returnValue({ offsetNode: text, offset: 13 } as unknown as CaretPosition);
      window.getSelection()?.removeAllRanges();

      row.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: x, clientY: y }));
      expect(writeText).toHaveBeenCalledWith('<10,13>');
    });

    it('copies the whole board with line numbers when nothing is selected', () => {
      openWith(snapshotWith(buildBoard()));
      const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      window.getSelection()?.removeAllRanges();

      component.copyWithLineNumbers();
      const copied = writeText.calls.mostRecent().args[0] as string;
      expect(copied.startsWith('L  1: Map:\n')).toBeTrue();
      expect(copied.split('\n').length).toBe(250);
    });
  });
});
