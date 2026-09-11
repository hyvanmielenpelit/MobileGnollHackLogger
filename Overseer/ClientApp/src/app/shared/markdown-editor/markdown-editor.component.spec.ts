import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';

import { MarkdownEditorComponent, computeMarkdownWarnings } from './markdown-editor.component';

describe('MarkdownEditorComponent', () => {
  let component: MarkdownEditorComponent;
  let fixture: ComponentFixture<MarkdownEditorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MarkdownEditorComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(MarkdownEditorComponent);
    component = fixture.componentInstance;
    component.inputId = 'testEditor';
    fixture.detectChanges();
  });

  it('renders both panes with the write pane visible and the preview pane hidden', () => {
    const write = fixture.nativeElement.querySelector('.md-editor-write');
    const preview = fixture.nativeElement.querySelector('.md-editor-preview');

    expect(write).toBeTruthy();
    expect(preview).toBeTruthy();
    expect(write.hasAttribute('hidden')).toBeFalse();
    expect(preview.hasAttribute('hidden')).toBeTrue();
  });

  it('selecting Preview flips aria-selected, flips hidden, and renders the Markdown', () => {
    component.value = '**x**';
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();

    component.selectMode('preview');
    fixture.detectChanges();

    const previewTab = fixture.nativeElement.querySelector('#testEditor-tab-preview');
    expect(previewTab.getAttribute('aria-selected')).toBe('true');

    const write = fixture.nativeElement.querySelector('.md-editor-write');
    const preview = fixture.nativeElement.querySelector('.md-editor-preview');
    expect(write.hasAttribute('hidden')).toBeTrue();
    expect(preview.hasAttribute('hidden')).toBeFalse();

    const strong = preview.querySelector('strong');
    expect(strong).toBeTruthy();
    expect(strong.textContent).toContain('x');
  });

  it('in Split, neither pane carries hidden, and the panel aria-labelledby points at the Split tab', () => {
    component.splitAvailable = true;
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();

    component.selectMode('split');
    fixture.detectChanges();

    const write = fixture.nativeElement.querySelector('.md-editor-write');
    const preview = fixture.nativeElement.querySelector('.md-editor-preview');
    expect(write.hasAttribute('hidden')).toBeFalse();
    expect(preview.hasAttribute('hidden')).toBeFalse();

    const panel = fixture.nativeElement.querySelector('.md-editor-panes');
    expect(panel.getAttribute('aria-labelledby')).toBe('testEditor-tab-split');
  });

  it('ArrowRight selects the next mode and moves focus, End jumps to Preview, and roving tabindex leaves exactly one tab at 0', () => {
    component.splitAvailable = true;
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();

    const writeTab: HTMLButtonElement = fixture.nativeElement.querySelector('#testEditor-tab-write');
    writeTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    fixture.detectChanges();

    expect(component.mode).toBe('split');
    expect(document.activeElement?.id).toBe('testEditor-tab-split');
    const tabs: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('.gh-tab'));
    expect(tabs.filter(t => t.getAttribute('tabindex') === '0').length).toBe(1);
    expect(tabs.find(t => t.id === 'testEditor-tab-split')?.getAttribute('tabindex')).toBe('0');

    const splitTab: HTMLButtonElement = fixture.nativeElement.querySelector('#testEditor-tab-split');
    splitTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
    fixture.detectChanges();

    expect(component.mode).toBe('preview');
  });

  it('below splitMinWidth the tablist renders two tabs, not three', () => {
    component.splitAvailable = false;
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();

    const tabs = fixture.nativeElement.querySelectorAll('.gh-tab');
    expect(tabs.length).toBe(2);
  });

  it('sanitises the preview so an injected onerror handler never reaches the rendered HTML', () => {
    component.value = '<img src=x onerror="alert(1)">';
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();

    component.selectMode('preview');
    fixture.detectChanges();

    const preview = fixture.nativeElement.querySelector('.md-editor-preview');
    expect(preview.innerHTML).not.toContain('onerror');
  });

  it('the Bold button wraps the current selection and emits valueChange with the wrapped text', fakeAsync(() => {
    component.value = 'hello world';
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    // ngModel writes the value into the textarea on a microtask, and the selection
    // below is meaningless until it has.
    tick();

    const textarea: HTMLTextAreaElement = fixture.nativeElement.querySelector('textarea');
    expect(textarea.value).toBe('hello world');
    textarea.focus();
    textarea.setSelectionRange(0, 5);

    let emitted: string | undefined;
    component.valueChange.subscribe((v: string) => (emitted = v));

    component.applyBold();
    fixture.detectChanges();

    expect(component.value).toBe('**hello** world');
    expect(emitted).toBe('**hello** world');

    tick(400);   // drains the debounced warnings timer the edit scheduled
  }));

  it('computes exactly one advisory per structural problem, and none for clean text', () => {
    expect(computeMarkdownWarnings('```\ncode with no closing fence').map(w => w.id)).toEqual(['fence']);
    expect(computeMarkdownWarnings('| a | b |\n| 1 | 2 |').map(w => w.id)).toEqual(['table']);
    expect(computeMarkdownWarnings('#Heading with no space').map(w => w.id)).toEqual(['heading']);

    const clean = [
      '# Heading',
      '',
      'Some ordinary text.',
      '',
      '| a | b |',
      '| --- | --- |',
      '| 1 | 2 |',
      '| 3 | 4 |',
      '',
      '```',
      'code, properly closed',
      '```'
    ].join('\n');
    expect(computeMarkdownWarnings(clean)).toEqual([]);
  });

  it('does not treat a table data row as a header missing its separator', () => {
    const table = ['| a | b |', '| --- | --- |', '| 1 | 2 |', '| 3 | 4 |', '| 5 | 6 |'].join('\n');
    expect(computeMarkdownWarnings(table)).toEqual([]);
  });

  it('warns once for a table whose header has no separator, however many rows follow', () => {
    const table = ['| a | b |', '| 1 | 2 |', '| 3 | 4 |'].join('\n');
    expect(computeMarkdownWarnings(table).map(w => w.id)).toEqual(['table']);
  });
});
