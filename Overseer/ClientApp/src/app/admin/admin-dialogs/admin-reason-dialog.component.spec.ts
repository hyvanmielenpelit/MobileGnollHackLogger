import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminReasonDialogComponent } from './admin-reason-dialog.component';

describe('AdminReasonDialogComponent', () => {
  let component: AdminReasonDialogComponent;
  let fixture: ComponentFixture<AdminReasonDialogComponent>;
  let host: HTMLElement;

  const dialog = () => host.querySelector('dialog') as HTMLDialogElement;
  const field = () => host.querySelector('textarea') as HTMLTextAreaElement;
  const confirmButton = () => host.querySelector('.reason-confirm') as HTMLButtonElement;
  const cancelButton = () => host.querySelector('.btn-gh-cancel') as HTMLButtonElement;

  function type(text: string): void {
    field().value = text;
    field().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AdminReasonDialogComponent] }).compileComponents();
    fixture = TestBed.createComponent(AdminReasonDialogComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog().open) {
      dialog().close();
    }
  });

  it('shows the title, message, label and length limit of the opening', () => {
    component.open({ title: 'Place deletion hold', message: 'Why?', label: 'Note', maxLength: 512, confirmText: 'Hold' });
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(host.querySelector('h3')?.textContent).toContain('Place deletion hold');
    expect(host.querySelector('.reason-message')?.textContent).toContain('Why?');
    expect(host.querySelector('label')?.textContent?.trim()).toBe('Note');
    expect(field().getAttribute('maxlength')).toBe('512');
    expect(field().required).toBe(true);
    expect(confirmButton().textContent?.trim()).toBe('Hold');
  });

  it('defaults the label to Reason and the length limit to 256', () => {
    component.open({ title: 'Block' });
    fixture.detectChanges();

    expect(host.querySelector('label')?.textContent?.trim()).toBe('Reason');
    expect(field().getAttribute('maxlength')).toBe('256');
  });

  it('requires a reason: confirm is aria-disabled and inert until non-whitespace text is typed', () => {
    const action = vi.fn();
    const emitted: string[] = [];
    component.confirmed.subscribe(r => emitted.push(r));
    component.open({ title: 'Block' }, action);
    fixture.detectChanges();

    expect(confirmButton().getAttribute('aria-disabled')).toBe('true');
    expect(host.querySelector('.gh-tooltip')?.textContent).toContain('A reason is required.');
    confirmButton().click();
    expect(action).not.toHaveBeenCalled();
    expect(dialog().open).toBe(true);

    type('   ');
    expect(confirmButton().getAttribute('aria-disabled')).toBe('true');
    confirmButton().click();
    expect(action).not.toHaveBeenCalled();
    expect(emitted).toEqual([]);

    type('Spam');
    expect(confirmButton().hasAttribute('aria-disabled')).toBe(false);
    expect(host.querySelector('.gh-tooltip')).toBeNull();
  });

  it('passes and emits the trimmed reason on confirm, and closes', () => {
    const action = vi.fn();
    const emitted: string[] = [];
    component.confirmed.subscribe(r => emitted.push(r));
    component.open({ title: 'Block' }, action);
    fixture.detectChanges();

    type('  Abusive prompts \n');
    confirmButton().click();
    fixture.detectChanges();

    expect(action).toHaveBeenCalledWith('Abusive prompts');
    expect(emitted).toEqual(['Abusive prompts']);
    expect(dialog().open).toBe(false);
  });

  it('emits nothing on cancel, and starts empty when opened again', () => {
    const action = vi.fn();
    const emitted: string[] = [];
    component.confirmed.subscribe(r => emitted.push(r));
    component.open({ title: 'Block' }, action);
    fixture.detectChanges();

    type('Abusive prompts');
    cancelButton().click();
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    expect(action).not.toHaveBeenCalled();
    expect(emitted).toEqual([]);

    component.open({ title: 'Block' });
    fixture.detectChanges();
    expect(field().value).toBe('');
    expect(confirmButton().getAttribute('aria-disabled')).toBe('true');
  });
});
