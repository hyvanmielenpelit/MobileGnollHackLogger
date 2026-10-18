import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ReindexConfirmDialogComponent } from './reindex-confirm-dialog.component';

/** A dialog's close event is queued as a task; wait for the event itself. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

const MESSAGE = 'A GnollBench run is executing and reads the GnollHack wiki.';

describe('ReindexConfirmDialogComponent', () => {
  let fixture: ComponentFixture<ReindexConfirmDialogComponent>;
  let component: ReindexConfirmDialogComponent;
  let confirmedCount: number;

  const dialog = () => (fixture.nativeElement as HTMLElement).querySelector('dialog') as HTMLDialogElement;
  const q = <T extends Element = HTMLElement>(selector: string) => dialog().querySelector<T>(selector);
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ReindexConfirmDialogComponent] }).compileComponents();
    fixture = TestBed.createComponent(ReindexConfirmDialogComponent);
    component = fixture.componentInstance;
    confirmedCount = 0;
    component.confirmed.subscribe(() => confirmedCount++);
    fixture.componentRef.setInput('idPrefix', 'gw');
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog()?.open) {
      dialog().close();
    }
  });

  it('is closed until opened', () => {
    expect(dialog().open).toBe(false);
    expect(dialog().classList).toContain('gh-dialog');
  });

  it('opens modally with the server message, named by its heading, with Cancel focused', () => {
    component.open(MESSAGE);
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(dialog().matches(':modal')).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBe('gw-reindex-confirm-title');
    expect(dialog().getAttribute('aria-describedby')).toBe('gw-reindex-confirm-message gw-reindex-confirm-effect');
    expect(text(q('#gw-reindex-confirm-title'))).toBe('A GnollBench run is executing');
    expect(text(q('#gw-reindex-confirm-message'))).toBe(MESSAGE);
    expect(text(q('#gw-reindex-confirm-effect'))).toBe(
      "Re-index changes the corpus the run is reading; questions after this point read the new index, while the run's recorded fingerprints describe the old one.");
    expect(document.activeElement).toBe(q('#gw-reindex-confirm-cancel'));
  });

  it('has a text-only Cancel first and the confirm button second', () => {
    component.open(MESSAGE);
    fixture.detectChanges();
    const buttons = Array.from(dialog().querySelectorAll<HTMLButtonElement>('.dialog-actions button'));
    expect(buttons.map(b => text(b))).toEqual(['Cancel', 'Re-index anyway']);
    expect(buttons.every(b => b.type === 'button')).toBe(true);
    expect(buttons[0].classList).toContain('btn-gh-cancel');
    expect(buttons[1].classList).toContain('btn-gh');
    expect(buttons[1].classList).not.toContain('btn-gh-cancel');
    expect(dialog().querySelector('.dialog-actions svg')).toBeNull();
  });

  it('names the action on the confirm button and in the effect sentence', () => {
    fixture.componentRef.setInput('confirmLabel', 'Reload anyway');
    fixture.componentRef.setInput('actionName', 'Reload');
    fixture.detectChanges();
    component.open(MESSAGE);
    fixture.detectChanges();
    expect(text(q('#gw-reindex-confirm-ok'))).toBe('Reload anyway');
    expect(text(q('#gw-reindex-confirm-effect'))).toMatch(/^Reload changes the corpus/);
  });

  it('closes on Cancel without confirming', async () => {
    component.open(MESSAGE);
    fixture.detectChanges();
    const closing = nextClose(dialog());
    q<HTMLButtonElement>('#gw-reindex-confirm-cancel')!.click();
    await closing;
    expect(dialog().open).toBe(false);
    expect(confirmedCount).toBe(0);
  });

  it('closes on the confirm button and emits confirmed once', async () => {
    component.open(MESSAGE);
    fixture.detectChanges();
    const closing = nextClose(dialog());
    q<HTMLButtonElement>('#gw-reindex-confirm-ok')!.click();
    await closing;
    expect(dialog().open).toBe(false);
    expect(confirmedCount).toBe(1);
  });

  it('does not confirm when the platform closes it, as Escape does', async () => {
    component.open(MESSAGE);
    fixture.detectChanges();
    const closing = nextClose(dialog());
    dialog().close();
    await closing;
    expect(confirmedCount).toBe(0);
  });

  it('shows a new message when opened again', async () => {
    component.open(MESSAGE);
    fixture.detectChanges();
    const closing = nextClose(dialog());
    component.close();
    await closing;

    component.open('A GnollBench run is executing and reads the knowledge base.');
    fixture.detectChanges();
    expect(text(q('#gw-reindex-confirm-message'))).toBe('A GnollBench run is executing and reads the knowledge base.');
    expect(dialog().open).toBe(true);
  });
});
