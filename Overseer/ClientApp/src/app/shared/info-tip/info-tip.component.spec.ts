import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { InfoTipComponent } from './info-tip.component';

@Component({
  standalone: true,
  imports: [InfoTipComponent],
  template: '<app-info-tip tipId="demo-tip" subject="Outline width">Outlined bars need at least 1 px.</app-info-tip>'
})
class HostComponent {}

@Component({
  standalone: true,
  imports: [InfoTipComponent],
  template: '<app-info-tip trigger="click" tipId="click-tip" subject="Scoring Profile">Weights the rubric dimensions.</app-info-tip>'
})
class ClickHostComponent {}

@Component({
  standalone: true,
  imports: [InfoTipComponent],
  template: `
    <div class="outer-row" (click)="rowClicks = rowClicks + 1">
      <app-info-tip trigger="dialog" tipId="dlg-tip" subject="Report writer" dialogTitle="Choosing a report writer">
        <p>Use a strong writing model.</p>
      </app-info-tip>
    </div>`
})
class DialogHostComponent {
  rowClicks = 0;
}

@Component({
  standalone: true,
  imports: [InfoTipComponent],
  template: `
    <dialog class="outer-dialog" (cancel)="events.push('cancel')" (close)="events.push('close')">
      <app-info-tip trigger="dialog" tipId="nested-tip" subject="Versions">Summary, Detailed and Full.</app-info-tip>
    </dialog>`
})
class NestedDialogHostComponent {
  readonly events: string[] = [];
}

describe('InfoTipComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  function button(): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector('button.gh-info-btn') as HTMLButtonElement;
  }

  function tip(): HTMLElement {
    return (fixture.nativeElement as HTMLElement).querySelector('#demo-tip') as HTMLElement;
  }

  it('renders a named button that points at its hint tooltip', () => {
    expect(button().type).toBe('button');
    expect(button().getAttribute('aria-label')).toBe('About Outline width');
    expect(button().getAttribute('interestfor')).toBe('demo-tip');
    expect(button().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders the tooltip as a hint popover with no role and the projected text', () => {
    expect(tip().getAttribute('popover')).toBe('hint');
    expect(tip().getAttribute('role')).toBeNull();
    expect(tip().classList).toContain('gh-tooltip');
    expect(tip().classList).toContain('gh-tooltip-multiline');
    expect(tip().textContent?.trim()).toBe('Outlined bars need at least 1 px.');
  });

  it('anchors the tooltip to the button by a matching name', () => {
    expect(button().getAttribute('style')).toContain('anchor-name: --demo-tip');
    expect(tip().getAttribute('style')).toContain('position-anchor: --demo-tip');
  });
});

describe('InfoTipComponent (click trigger)', () => {
  let fixture: ComponentFixture<ClickHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ClickHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(ClickHostComponent);
    fixture.detectChanges();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(): HTMLButtonElement {
    return root().querySelector('button.gh-info-btn') as HTMLButtonElement;
  }

  function popup(): HTMLElement {
    return root().querySelector('#click-tip-popup') as HTMLElement;
  }

  function toggleEvent(newState: 'open' | 'closed'): Event {
    if (typeof ToggleEvent !== 'undefined') {
      return new ToggleEvent('toggle', { oldState: newState === 'open' ? 'closed' : 'open', newState });
    }
    const event = new Event('toggle');
    Object.defineProperty(event, 'newState', { value: newState });
    return event;
  }

  it('renders a named button that toggles its popup', () => {
    expect(button().type).toBe('button');
    expect(button().classList).toContain('gh-info-btn--click');
    expect(button().getAttribute('aria-label')).toBe('About Scoring Profile');
    expect(button().getAttribute('popovertarget')).toBe('click-tip-popup');
    expect(button().getAttribute('aria-expanded')).toBe('false');
    expect(button().hasAttribute('interestfor')).toBe(false);
    expect(button().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders an auto popover with no role, titled by the subject, and the text under tipId', () => {
    expect(popup().getAttribute('popover')).toBe('auto');
    expect(popup().getAttribute('role')).toBeNull();
    expect(popup().classList).toContain('gh-info-popup');
    expect(popup().getAttribute('aria-labelledby')).toBe('click-tip-title');
    expect(root().querySelector('#click-tip-title')?.textContent?.trim()).toBe('Scoring Profile');
    expect(root().querySelector('#click-tip')?.textContent?.trim()).toBe('Weights the rubric dimensions.');
    expect(root().querySelector('.gh-tooltip')).toBeNull();
  });

  it('reflects the popup state in aria-expanded', () => {
    popup().dispatchEvent(toggleEvent('open'));
    fixture.detectChanges();
    expect(button().getAttribute('aria-expanded')).toBe('true');

    popup().dispatchEvent(toggleEvent('closed'));
    fixture.detectChanges();
    expect(button().getAttribute('aria-expanded')).toBe('false');
  });

  it('anchors the popup to the button by a matching name', () => {
    expect(button().getAttribute('style')).toContain('anchor-name: --click-tip');
    expect(popup().getAttribute('style')).toContain('position-anchor: --click-tip');
  });
});

describe('InfoTipComponent (dialog trigger)', () => {
  let fixture: ComponentFixture<DialogHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DialogHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(DialogHostComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(): HTMLButtonElement {
    return root().querySelector('button.gh-info-btn') as HTMLButtonElement;
  }

  function dialog(): HTMLDialogElement {
    return root().querySelector('app-info-tip dialog') as HTMLDialogElement;
  }

  function closeButton(): HTMLButtonElement {
    return dialog().querySelector('.dialog-header .btn-icon-action') as HTMLButtonElement;
  }

  it('renders a named button that opens a dialog, with no popover or tooltip of its own', () => {
    expect(button().type).toBe('button');
    expect(button().classList).toContain('gh-info-btn--click');
    expect(button().getAttribute('aria-label')).toBe('About Report writer');
    expect(button().getAttribute('aria-haspopup')).toBe('dialog');
    expect(button().hasAttribute('popovertarget')).toBe(false);
    expect(button().hasAttribute('interestfor')).toBe(false);
    expect(button().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(root().querySelector('.gh-info-popup')).toBeNull();
  });

  it('keeps a closed light-dismiss dialog titled by dialogTitle, its ids derived from tipId', () => {
    expect(dialog().open).toBe(false);
    expect(dialog().classList).toContain('gh-dialog');
    expect(dialog().classList).toContain('gh-info-dialog');
    expect(dialog().getAttribute('closedby')).toBe('any');
    expect(dialog().getAttribute('aria-labelledby')).toBe('dlg-tip-title');
    const title = root().querySelector('#dlg-tip-title') as HTMLElement;
    expect(title.tagName).toBe('H3');
    expect(title.getAttribute('tabindex')).toBe('-1');
    expect(title.textContent?.trim()).toBe('Choosing a report writer');
    const body = root().querySelector('#dlg-tip') as HTMLElement;
    expect(body.classList).toContain('gh-info-dialog-body');
    expect(dialog().contains(body)).toBe(true);
    expect(body.textContent?.trim()).toBe('Use a strong writing model.');
    expect(root().querySelector('#dlg-tip-close-tip')).not.toBeNull();
  });

  it('opens modally from the button and focuses its title', () => {
    button().click();
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(dialog().matches(':modal')).toBe(true);
    expect(document.activeElement).toBe(root().querySelector('#dlg-tip-title'));
  });

  it('names the close button after the title, gives it a hint tooltip, and closes with it', () => {
    button().click();
    fixture.detectChanges();
    const close = closeButton();
    expect(close.getAttribute('aria-label')).toBe('Close Choosing a report writer');
    expect(close.hasAttribute('title')).toBe(false);
    expect(close.getAttribute('interestfor')).toBe('dlg-tip-close-tip');
    expect(close.getAttribute('style')).toContain('anchor-name: --dlg-tip-close-tip');
    const tip = root().querySelector('#dlg-tip-close-tip') as HTMLElement;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(tip.getAttribute('style')).toContain('position-anchor: --dlg-tip-close-tip');
    expect(tip.textContent?.trim()).toBe('Close');

    close.click();
    fixture.detectChanges();
    expect(dialog().open).toBe(false);
  });

  it('keeps clicks inside the dialog from reaching the control it sits in', () => {
    button().click();
    fixture.detectChanges();
    const clicksBefore = fixture.componentInstance.rowClicks;
    (root().querySelector('#dlg-tip p') as HTMLElement).click();
    expect(fixture.componentInstance.rowClicks).toBe(clicksBefore);
    expect(dialog().open).toBe(true);
  });

  it('falls back to the subject for the title', () => {
    const tip = TestBed.createComponent(InfoTipComponent);
    tip.componentRef.setInput('tipId', 'plain-tip');
    tip.componentRef.setInput('subject', 'Versions');
    tip.componentRef.setInput('trigger', 'dialog');
    tip.detectChanges();
    const element = tip.nativeElement as HTMLElement;
    expect(element.querySelector('#plain-tip-title')?.textContent?.trim()).toBe('Versions');
    expect(element.querySelector('.btn-icon-action')?.getAttribute('aria-label')).toBe('Close Versions');
    tip.destroy();
  });
});

describe('InfoTipComponent (dialog trigger inside another dialog)', () => {
  let fixture: ComponentFixture<NestedDialogHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [NestedDialogHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(NestedDialogHostComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
  });

  function outer(): HTMLDialogElement {
    return (fixture.nativeElement as HTMLElement).querySelector('dialog.outer-dialog') as HTMLDialogElement;
  }

  function inner(): HTMLDialogElement {
    return (fixture.nativeElement as HTMLElement).querySelector('dialog.gh-info-dialog') as HTMLDialogElement;
  }

  it('stops its own cancel and close events, so the dialog around it stays open', async () => {
    outer().showModal();
    ((fixture.nativeElement as HTMLElement).querySelector('button.gh-info-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(inner().open).toBe(true);
    expect(document.activeElement).toBe((fixture.nativeElement as HTMLElement).querySelector('#nested-tip-title'));

    inner().dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
    inner().dispatchEvent(new Event('close', { bubbles: true }));
    (inner().querySelector('.btn-icon-action') as HTMLButtonElement).click();
    // The native close event is queued as a task.
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();

    expect(inner().open).toBe(false);
    expect(outer().open).toBe(true);
    expect(fixture.componentInstance.events).toEqual([]);
  });
});
