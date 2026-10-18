import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminDevtoolsTabComponent } from './admin-devtools-tab.component';
import { AdminService } from '../../services/admin.service';
import { AdminDevtoolsService } from '../../services/admin-devtools.service';
import { AdminPageStore } from '../admin-page.store';
import { configureAdminTestBed } from '../admin.component.testing';

describe('AdminDevtoolsTabComponent', () => {
  let fixture: ComponentFixture<AdminDevtoolsTabComponent>;
  let adminService: AdminService;
  let devtoolsService: AdminDevtoolsService;
  let store: AdminPageStore;

  beforeEach(async () => {
    ({ adminService, devtoolsService, store } = await configureAdminTestBed(AdminDevtoolsTabComponent));
    fixture = TestBed.createComponent(AdminDevtoolsTabComponent);
  });

  afterEach(() => fixture.destroy());

  const host = () => fixture.nativeElement as HTMLElement;
  const tab = (id: string) => host().querySelector(`#devtools-subtab-${id}`) as HTMLButtonElement;
  const press = (target: HTMLElement, key: string) => {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  };

  it('shows the Developer Tools heading', () => {
    fixture.detectChanges();

    expect(host().querySelector('.tab-header-row h3')?.textContent?.trim()).toBe('Developer Tools');
  });

  it('renders three sub-tabs in order with the tab contract, GnollHack Source selected', () => {
    fixture.detectChanges();

    const list = host().querySelector('[role="tablist"]') as HTMLElement;
    expect(list.getAttribute('aria-label')).toBe('Developer tool sections');
    expect(list.classList).toContain('gh-tabs');
    expect(list.classList).toContain('gh-tabs-secondary');
    const tabs = Array.from(list.querySelectorAll('[role="tab"]')) as HTMLButtonElement[];
    expect(tabs.map(t => t.id)).toEqual(['devtools-subtab-source', 'devtools-subtab-config-health', 'devtools-subtab-sentry']);
    expect(tabs.map(t => t.textContent?.trim())).toEqual(['GnollHack Source', 'Configuration Health', 'Sentry Crash Reporting']);
    expect(tabs.every(t => t.getAttribute('type') === 'button')).toBe(true);
    expect(tabs.map(t => t.getAttribute('aria-controls')))
      .toEqual(['devtools-subpanel-source', 'devtools-subpanel-config-health', 'devtools-subpanel-sentry']);
    expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
    expect(tabs.map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);

    const panel = host().querySelector('#devtools-subpanel-source') as HTMLElement;
    expect(panel.getAttribute('role')).toBe('tabpanel');
    expect(panel.getAttribute('aria-labelledby')).toBe('devtools-subtab-source');
    expect(panel.getAttribute('tabindex')).toBe('0');
    expect(panel.querySelector('app-source-dashboard')).toBeTruthy();
    expect(host().querySelector('#devtools-subpanel-config-health')).toBeNull();
    expect(host().querySelector('#devtools-subpanel-sentry')).toBeNull();
  });

  it('moves and selects with Right, Left, Home and End through the page store, replacing the history entry', () => {
    fixture.detectChanges();
    const goTo = vi.spyOn(store, 'goTo');

    press(tab('source'), 'ArrowRight');
    expect(goTo).toHaveBeenLastCalledWith('devtools', 'config-health', 'replace');
    expect(tab('config-health').getAttribute('aria-selected')).toBe('true');
    expect(tab('config-health').getAttribute('tabindex')).toBe('0');
    expect(tab('source').getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(tab('config-health'));
    expect(host().querySelector('#devtools-subpanel-config-health app-config-health')).toBeTruthy();
    expect(host().querySelector('#devtools-subpanel-source')).toBeNull();

    press(tab('config-health'), 'ArrowRight');
    expect(store.devtoolsSubTab).toBe('sentry');
    expect(document.activeElement).toBe(tab('sentry'));

    press(tab('sentry'), 'ArrowRight');
    expect(store.devtoolsSubTab).toBe('source');

    press(tab('source'), 'ArrowLeft');
    expect(store.devtoolsSubTab).toBe('sentry');

    press(tab('sentry'), 'Home');
    expect(store.devtoolsSubTab).toBe('source');
    expect(document.activeElement).toBe(tab('source'));

    press(tab('source'), 'End');
    expect(store.devtoolsSubTab).toBe('sentry');
    expect(goTo).toHaveBeenLastCalledWith('devtools', 'sentry', 'replace');
  });

  it('selects on a click with a new history entry, and not again for the selected sub-tab', () => {
    fixture.detectChanges();
    const goTo = vi.spyOn(store, 'goTo');

    tab('sentry').click();
    fixture.detectChanges();
    expect(goTo).toHaveBeenCalledWith('devtools', 'sentry', 'push');
    expect(tab('sentry').getAttribute('aria-selected')).toBe('true');

    goTo.mockClear();
    tab('sentry').click();
    expect(goTo).not.toHaveBeenCalled();
  });

  it('opens on the sub-tab the page store holds', () => {
    store.devtoolsSubTab = 'config-health';
    fixture.detectChanges();

    expect(tab('config-health').getAttribute('aria-selected')).toBe('true');
    expect(host().querySelector('#devtools-subpanel-config-health app-config-health')).toBeTruthy();
    expect(host().querySelector('#devtools-subpanel-source')).toBeNull();
    expect(devtoolsService.getConfigHealth).toHaveBeenCalled();
  });

  it('keeps the Sentry test buttons in the third panel', () => {
    const backend = vi.spyOn(adminService, 'triggerBackendSentryError').mockReturnValue(of({}));
    const toast = vi.spyOn(store, 'showToast');
    store.devtoolsSubTab = 'sentry';
    fixture.detectChanges();

    const panel = host().querySelector('#devtools-subpanel-sentry') as HTMLElement;
    expect(panel.querySelector('h4')?.textContent?.trim()).toBe('Sentry Crash Reporting');
    expect(panel.querySelector('fieldset')).toBeNull();
    const buttons = Array.from(panel.querySelectorAll('.devtools-button-group .btn-gh')) as HTMLButtonElement[];
    expect(buttons.map(b => b.textContent?.trim())).toEqual(['Test Frontend Error', 'Test Backend Error']);
    expect(panel.querySelector('.devtools-warning')?.textContent).toContain('send real error reports');

    buttons[1].click();

    expect(backend).toHaveBeenCalledTimes(1);
    expect(toast).toHaveBeenCalledWith('Sending backend crash request...', 'info', 'Sentry Test');
  });
});
