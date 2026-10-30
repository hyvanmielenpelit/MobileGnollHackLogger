import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ChatTurnNotice } from '../../services/chat.service';
import { TurnNoticeComponent } from './turn-notice.component';

const NOTICE: ChatTurnNotice = {
  code: 'research_rounds',
  severity: 'warning',
  title: 'Research limit reached',
  message: 'The Overseer used all the research steps allowed for one question.',
  advice: 'Ask it to continue.',
  action: 'continue'
};

describe('TurnNoticeComponent', () => {
  let fixture: ComponentFixture<TurnNoticeComponent>;

  const host = () => fixture.nativeElement as HTMLElement;
  const section = () => host().querySelector('section')!;

  function render(notice: ChatTurnNotice, actionAvailable = false): void {
    fixture.componentRef.setInput('notice', notice);
    fixture.componentRef.setInput('actionAvailable', actionAvailable);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TurnNoticeComponent] }).compileComponents();
    fixture = TestBed.createComponent(TurnNoticeComponent);
  });

  it('is a section named by its title, with the text and no live role', () => {
    render(NOTICE);

    const labelledBy = section().getAttribute('aria-labelledby')!;
    expect(host().querySelector(`[id="${labelledBy}"]`)!.textContent!.trim()).toBe(NOTICE.title);
    expect(section().textContent).toContain(NOTICE.message);
    expect(section().textContent).toContain(NOTICE.advice);
    expect(section().getAttribute('role')).toBeNull();
    expect(host().getAttribute('role')).toBeNull();
    expect(section().querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('tells a warning from an error by its class and its icon', () => {
    render(NOTICE);
    const warningIcon = section().querySelector('svg')!.innerHTML;
    expect(section().classList.contains('alert-warning')).toBe(true);
    expect(section().classList.contains('alert-danger')).toBe(false);

    const errorFixture = TestBed.createComponent(TurnNoticeComponent);
    errorFixture.componentRef.setInput('notice', { ...NOTICE, severity: 'error' });
    errorFixture.detectChanges();
    const errorSection = (errorFixture.nativeElement as HTMLElement).querySelector('section')!;
    expect(errorSection.classList.contains('alert-danger')).toBe(true);
    expect(errorSection.classList.contains('alert-warning')).toBe(false);
    expect(errorSection.querySelector('svg')!.innerHTML).not.toBe(warningIcon);
  });

  it('shows the reference only when there is one', () => {
    render({ ...NOTICE, reference: 1234 });
    expect(host().querySelector('.turn-notice-reference')!.textContent!.trim()).toBe('Reference L-1234');

    render({ ...NOTICE, reference: null });
    expect(host().querySelector('.turn-notice-reference')).toBeNull();
  });

  it('offers the action only when the host allows it, under the action\'s own label', () => {
    render(NOTICE);
    expect(host().querySelector('button')).toBeNull();

    render(NOTICE, true);
    expect(host().querySelector('button')!.textContent!.trim()).toBe('Continue');
    expect(host().querySelector('button')!.classList.contains('btn-ghost')).toBe(true);

    render({ ...NOTICE, action: 'try_again' }, true);
    expect(host().querySelector('button')!.textContent!.trim()).toBe('Try again');

    render({ ...NOTICE, action: 'new_chat' }, true);
    expect(host().querySelector('button')!.textContent!.trim()).toBe('New chat');

    render({ ...NOTICE, action: 'none' }, true);
    expect(host().querySelector('button')).toBeNull();
  });

  it('emits when the action is clicked', () => {
    const triggered = vi.fn();
    fixture.componentInstance.actionTriggered.subscribe(triggered);
    render(NOTICE, true);

    host().querySelector('button')!.click();

    expect(triggered).toHaveBeenCalledTimes(1);
  });

  it('lists the facts in a details element, as text, and has none without facts', () => {
    render(NOTICE);
    expect(host().querySelector('details')).toBeNull();

    render({
      ...NOTICE,
      adminFacts: { httpStatus: 429, totalDurationMs: 129100, providerErrorBody: '<b>rate limited</b>', somethingNew: 'x' }
    });

    const details = host().querySelector('details')!;
    expect(details.querySelector('summary')!.textContent!.trim()).toBe('Details');
    expect(details.querySelector('summary h1, summary h2, summary h3, summary h4, summary h5, summary h6')).toBeNull();
    const terms = Array.from(details.querySelectorAll('dt')).map(e => e.textContent!.trim());
    const values = Array.from(details.querySelectorAll('dd')).map(e => e.textContent!.trim());
    expect(terms).toEqual(['HTTP status', 'Duration', 'Provider response', 'somethingNew']);
    expect(values).toEqual(['429', '129.1 s', '<b>rate limited</b>', 'x']);
    expect(details.querySelector('b')).toBeNull();
  });
});
