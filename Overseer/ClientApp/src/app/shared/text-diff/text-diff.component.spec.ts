import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DiffResult, diffLines } from '../../utils/text-diff.util';
import { TextDiffComponent, diffLinePrefix } from './text-diff.component';

describe('TextDiffComponent', () => {
  let fixture: ComponentFixture<TextDiffComponent>;
  let host: HTMLElement;

  const linesOf = (selector: string): string[] =>
    Array.from(host.querySelectorAll(`${selector} .diff-line`)).map(l => l.textContent ?? '');

  function render(diff: DiffResult, view: 'side' | 'diff'): void {
    fixture.componentRef.setInput('diff', diff);
    fixture.componentRef.setInput('view', view);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TextDiffComponent] }).compileComponents();
    fixture = TestBed.createComponent(TextDiffComponent);
    host = fixture.nativeElement as HTMLElement;
  });

  it('prefixes each line by its kind', () => {
    expect(diffLinePrefix({ kind: 'added', text: 'a' })).toBe('+ ');
    expect(diffLinePrefix({ kind: 'removed', text: 'a' })).toBe('- ');
    expect(diffLinePrefix({ kind: 'equal', text: 'a' })).toBe('  ');
  });

  it('shows one column of marked lines in the diff view', () => {
    render(diffLines('one\ntwo', 'one\n2'), 'diff');

    expect(host.querySelectorAll('pre.import-diff').length).toBe(1);
    expect(linesOf('.text-diff-unified')).toEqual(['  one', '- two', '+ 2']);
    expect(host.querySelector('.diff-removed')!.textContent).toBe('- two');
    expect(host.querySelector('.diff-added')!.textContent).toBe('+ 2');
    expect(host.querySelector('.diff-equal')!.textContent).toBe('  one');
  });

  it('shows the text before on the left and after on the right in the side view', () => {
    render(diffLines('one\ntwo', 'one\n2'), 'side');

    expect(host.querySelectorAll('pre.import-diff').length).toBe(2);
    expect(linesOf('.text-diff-before')).toEqual(['one', 'two']);
    expect(linesOf('.text-diff-after')).toEqual(['one', '2']);
    expect(host.querySelector('.text-diff-before .diff-removed')!.textContent).toBe('two');
    expect(host.querySelector('.text-diff-after .diff-added')!.textContent).toBe('2');
    const groups = Array.from(host.querySelectorAll('[role="group"]')).map(g => g.getAttribute('aria-label'));
    expect(groups).toEqual(['Before', 'After']);
  });

  it('names the columns as the host asks', () => {
    fixture.componentRef.setInput('beforeLabel', 'Current note');
    fixture.componentRef.setInput('afterLabel', 'Imported note');
    render(diffLines('a', 'b'), 'side');
    const groups = Array.from(host.querySelectorAll('[role="group"]')).map(g => g.getAttribute('aria-label'));
    expect(groups).toEqual(['Current note', 'Imported note']);
  });

  it('switches view when the input changes', () => {
    render(diffLines('a', 'b'), 'side');
    expect(host.querySelector('.text-diff-side')).not.toBeNull();
    render(diffLines('a', 'b'), 'diff');
    expect(host.querySelector('.text-diff-side')).toBeNull();
    expect(host.querySelector('.text-diff-unified')).not.toBeNull();
  });

  it('marks an empty side as empty', () => {
    render(diffLines('', 'new text'), 'side');
    expect(linesOf('.text-diff-before')).toEqual(['(empty)']);
    expect(linesOf('.text-diff-after')).toEqual(['new text']);

    render(diffLines('', ''), 'diff');
    expect(linesOf('.text-diff-unified')).toEqual(['(empty)']);
  });

  it('says so when the diff was too long to align', () => {
    render({ lines: [{ kind: 'removed', text: 'a' }, { kind: 'added', text: 'b' }], truncated: true }, 'diff');
    expect(host.querySelector('.text-diff-truncated')).not.toBeNull();

    render(diffLines('a', 'b'), 'diff');
    expect(host.querySelector('.text-diff-truncated')).toBeNull();
  });

  it('renders markup in the text as text', () => {
    render(diffLines('', '<b>bold</b>'), 'diff');
    expect(host.querySelector('b')).toBeNull();
    expect(linesOf('.text-diff-unified')).toEqual(['+ <b>bold</b>']);
  });
});
