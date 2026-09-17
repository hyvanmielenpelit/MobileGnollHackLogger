import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilePickerComponent, acceptExtensions, matchesAccept } from './file-picker.component';

/** Owns the file state the way a real host does: a pick attaches, the remove button clears. */
@Component({
  standalone: true,
  imports: [FilePickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" class="outside">Outside</button>
    <app-file-picker inputId="test-file" label="YAML file" [optional]="optional"
                     accept=".yaml,.yml,text/plain" acceptHint=".yaml or .yml, up to 2 MB"
                     describedBy="test-file-hint"
                     [fileName]="fileName" [fileDetail]="fileDetail" [error]="error"
                     (fileSelected)="onSelected($event)" (cleared)="onCleared()" />
    <p id="test-file-hint">Hint</p>
  `
})
class TestHostComponent {
  private cdr = inject(ChangeDetectorRef);
  optional = false;
  fileName: string | null = null;
  fileDetail: string | null = null;
  error: string | null = null;
  selected: File[] = [];
  clearedCount = 0;

  update(patch: Partial<Pick<TestHostComponent, 'optional' | 'error'>>): void {
    Object.assign(this, patch);
    this.cdr.markForCheck();
  }

  onSelected(file: File): void {
    this.selected.push(file);
    this.fileName = file.name;
    this.fileDetail = `${file.size} B`;
    this.cdr.markForCheck();
  }

  onCleared(): void {
    this.clearedCount++;
    this.fileName = null;
    this.fileDetail = null;
    this.cdr.markForCheck();
  }
}

describe('FilePickerComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let hostComponent: TestHostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(TestHostComponent);
    hostComponent = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  const fileInput = () => el.querySelector<HTMLInputElement>('input[type="file"]');
  const card = () => el.querySelector<HTMLElement>('.gh-file-card');
  const removeButton = () => el.querySelector<HTMLButtonElement>('.gh-file-card button');

  function pick(file: File): void {
    const input = fileInput()!;
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function drop(target: HTMLElement, ...files: File[]): DragEvent {
    const transfer = new DataTransfer();
    files.forEach(f => transfer.items.add(f));
    const event = new DragEvent('drop', { dataTransfer: transfer, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  const yaml = (name = 'agent-new-questions-core.yaml') => new File(['format: x'], name, { type: 'text/plain' });

  it('reads the accept extensions', () => {
    expect(acceptExtensions('.yaml, .YML,text/plain')).toEqual(['.yaml', '.yml']);
    expect(matchesAccept('a.Yaml', '.yaml,.yml')).toBeTrue();
    expect(matchesAccept('a.png', '.yaml,.yml')).toBeFalse();
    expect(matchesAccept('a.png', 'text/plain')).toBeTrue();
  });

  it('renders a zone and a labelled, described file input while empty', () => {
    const input = fileInput()!;
    expect(el.querySelector('.gh-file-zone')).not.toBeNull();
    expect(el.querySelector(`label[for="test-file"]`)).not.toBeNull();
    expect(input.id).toBe('test-file');
    expect(input.getAttribute('accept')).toBe('.yaml,.yml,text/plain');
    expect(input.getAttribute('aria-labelledby')).toContain('test-file-label');
    expect(input.getAttribute('aria-describedby')).toBe('test-file-accept test-file-hint');
    expect(el.querySelector('#test-file-label')!.textContent).toContain('YAML file');
    expect(el.querySelector('.gh-file-optional')).toBeNull();
    expect(el.querySelector('.gh-file-status[role="status"]')).not.toBeNull();
  });

  it('marks an optional picker', () => {
    hostComponent.update({ optional: true });
    fixture.detectChanges();
    expect(el.querySelector('.gh-file-optional')!.textContent).toBe('(optional)');
  });

  it('emits the picked file and resets the native value', () => {
    const input = fileInput()!;
    const transfer = new DataTransfer();
    transfer.items.add(yaml());
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    expect(hostComponent.selected.map(f => f.name)).toEqual(['agent-new-questions-core.yaml']);
    expect(input.value).toBe('');
    expect(input.files!.length).toBe(0);
  });

  it('replaces the zone with a file card and no file input once attached', () => {
    pick(yaml());
    expect(fileInput()).toBeNull();
    expect(el.querySelector('.gh-file-zone')).toBeNull();
    expect(card()!.querySelector('.gh-file-card-name')!.textContent).toBe('agent-new-questions-core.yaml');
    expect(card()!.querySelector('.gh-file-card-detail')!.textContent).toBe('9 B');
    expect(el.querySelector('.gh-file-status')!.textContent).toBe('agent-new-questions-core.yaml attached, 9 B.');
  });

  it('names the file on the remove button, with a hint tooltip and no title', () => {
    pick(yaml());
    const button = removeButton()!;
    expect(button.getAttribute('aria-label')).toBe('Remove agent-new-questions-core.yaml');
    expect(button.hasAttribute('title')).toBeFalse();
    const tip = el.querySelector('#' + button.getAttribute('interestfor'))!;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(tip.textContent).toBe('Remove file');
    expect(tip.hasAttribute('role')).toBeFalse();
  });

  it('fires cleared on the remove button and returns to the zone', () => {
    pick(yaml());
    removeButton()!.click();
    fixture.detectChanges();
    expect(hostComponent.clearedCount).toBe(1);
    expect(card()).toBeNull();
    expect(fileInput()).not.toBeNull();
    expect(el.querySelector('.gh-file-status')!.textContent).toBe('File removed.');
  });

  it('moves focus to the card after attaching and to the input after removing', () => {
    fileInput()!.focus();
    pick(yaml());
    expect(document.activeElement).toBe(card());

    removeButton()!.focus();
    removeButton()!.click();
    fixture.detectChanges();
    expect(document.activeElement).toBe(fileInput());
  });

  it('attaches a dropped file with a matching extension', () => {
    const event = drop(el.querySelector('.gh-file-zone')!, yaml('x.YML'));
    expect(event.defaultPrevented).toBeTrue();
    expect(hostComponent.selected.map(f => f.name)).toEqual(['x.YML']);
    expect(card()).not.toBeNull();
  });

  it('refuses a dropped file with another extension, and says why', () => {
    drop(el.querySelector('.gh-file-zone')!, new File(['x'], 'board.png'));
    expect(hostComponent.selected).toEqual([]);
    const error = el.querySelector('#test-file-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent).toBe('board.png is not a .yaml or .yml file.');
    expect(fileInput()!.getAttribute('aria-describedby')).toContain('test-file-error');
  });

  it('uses the first of several dropped files and says so', () => {
    drop(el.querySelector('.gh-file-zone')!, yaml('a.yaml'), yaml('b.yaml'));
    expect(hostComponent.selected.map(f => f.name)).toEqual(['a.yaml']);
    expect(el.querySelector('.gh-file-status')!.textContent).toContain('Only the first of the 2 dropped files was used.');
  });

  it('replaces the attached file with one dropped on the card', () => {
    pick(yaml('a.yaml'));
    drop(card()!, yaml('b.yaml'));
    expect(card()!.querySelector('.gh-file-card-name')!.textContent).toBe('b.yaml');
  });

  it('shows the host error as an alert linked to the input', () => {
    hostComponent.update({ error: 'too big' });
    fixture.detectChanges();
    expect(el.querySelector('#test-file-error')!.textContent).toBe('too big');
    expect(fileInput()!.getAttribute('aria-describedby')).toBe('test-file-accept test-file-hint test-file-error');
  });
});
