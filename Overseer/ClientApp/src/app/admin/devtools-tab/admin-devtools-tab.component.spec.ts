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

  const ids = ['source', 'gnollhack-wiki', 'nethack-wiki', 'knowledge-base', 'config-health', 'sentry'];

  it('shows the Services heading', () => {
    fixture.detectChanges();

    expect(host().querySelector('.tab-header-row h3')?.textContent?.trim()).toBe('Services');
  });

  it('renders six sub-tabs in order with the tab contract, GnollHack Source selected', () => {
    fixture.detectChanges();

    const list = host().querySelector('[role="tablist"]') as HTMLElement;
    expect(list.getAttribute('aria-label')).toBe('Service sections');
    expect(list.classList).toContain('gh-tabs');
    expect(list.classList).toContain('gh-tabs-secondary');
    expect(list.classList).toContain('gh-tabs-wrap');
    const tabs = Array.from(list.querySelectorAll('[role="tab"]')) as HTMLButtonElement[];
    expect(tabs.map(t => t.id)).toEqual(ids.map(id => `devtools-subtab-${id}`));
    expect(tabs.map(t => t.textContent?.trim())).toEqual(
      ['GnollHack Source', 'GnollHack Wiki', 'NetHack Wiki', 'Knowledge Base', 'Configuration Health', 'Developer Tools']);
    expect(tabs.every(t => t.getAttribute('type') === 'button')).toBe(true);
    expect(tabs.map(t => t.getAttribute('aria-controls'))).toEqual(ids.map(id => `devtools-subpanel-${id}`));
    expect(tabs.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false', 'false', 'false', 'false']);
    expect(tabs.map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1', '-1', '-1']);

    const panel = host().querySelector('#devtools-subpanel-source') as HTMLElement;
    expect(panel.getAttribute('role')).toBe('tabpanel');
    expect(panel.getAttribute('aria-labelledby')).toBe('devtools-subtab-source');
    expect(panel.getAttribute('tabindex')).toBe('0');
    expect(panel.querySelector('app-source-dashboard')).toBeTruthy();
    for (const id of ids.slice(1)) {
      expect(host().querySelector(`#devtools-subpanel-${id}`)).toBeNull();
    }
  });

  for (const [id, selector] of [
    ['gnollhack-wiki', 'app-gnollhack-wiki-dashboard'],
    ['nethack-wiki', 'app-nethack-wiki-dashboard'],
    ['knowledge-base', 'app-knowledge-base-dashboard']
  ] as const) {
    it(`shows the ${id} dashboard in its own tab panel`, () => {
      store.devtoolsSubTab = id;
      fixture.detectChanges();

      const panel = host().querySelector(`#devtools-subpanel-${id}`) as HTMLElement;
      expect(panel.getAttribute('role')).toBe('tabpanel');
      expect(panel.getAttribute('aria-labelledby')).toBe(`devtools-subtab-${id}`);
      expect(panel.getAttribute('tabindex')).toBe('0');
      expect(panel.querySelector(selector)).toBeTruthy();
      expect(host().querySelector('#devtools-subpanel-source')).toBeNull();
    });
  }

  it('moves and selects with Right, Left, Home and End through the page store, replacing the history entry', () => {
    fixture.detectChanges();
    const goTo = vi.spyOn(store, 'goTo');

    press(tab('source'), 'ArrowRight');
    expect(goTo).toHaveBeenLastCalledWith('devtools', 'gnollhack-wiki', 'replace');
    expect(tab('gnollhack-wiki').getAttribute('aria-selected')).toBe('true');
    expect(tab('gnollhack-wiki').getAttribute('tabindex')).toBe('0');
    expect(tab('source').getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(tab('gnollhack-wiki'));
    expect(host().querySelector('#devtools-subpanel-gnollhack-wiki app-gnollhack-wiki-dashboard')).toBeTruthy();
    expect(host().querySelector('#devtools-subpanel-source')).toBeNull();

    press(tab('gnollhack-wiki'), 'ArrowRight');
    press(tab('nethack-wiki'), 'ArrowRight');
    press(tab('knowledge-base'), 'ArrowRight');
    expect(store.devtoolsSubTab).toBe('config-health');
    expect(host().querySelector('#devtools-subpanel-config-health app-config-health')).toBeTruthy();

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

  it('keeps the Sentry test buttons in a Sentry Crash Reporting fieldset in the Developer Tools panel', () => {
    const backend = vi.spyOn(adminService, 'triggerBackendSentryError').mockReturnValue(of({}));
    const toast = vi.spyOn(store, 'showToast');
    store.devtoolsSubTab = 'sentry';
    fixture.detectChanges();

    const panel = host().querySelector('#devtools-subpanel-sentry') as HTMLElement;
    expect(panel.querySelector('h1, h2, h3, h4, h5, h6')).toBeNull();
    const fieldset = panel.querySelector('fieldset.gh-fieldset') as HTMLFieldSetElement;
    expect(fieldset.querySelector('legend')?.textContent?.trim()).toBe('Sentry Crash Reporting');
    const buttons = Array.from(fieldset.querySelectorAll('.devtools-button-group .btn-gh')) as HTMLButtonElement[];
    expect(buttons.map(b => b.textContent?.trim())).toEqual(['Test Frontend Error', 'Test Backend Error']);
    expect(buttons.every(b => b.getAttribute('type') === 'button')).toBe(true);
    expect(buttons.every(b => b.getAttribute('aria-describedby') === 'devtools-sentry-warning')).toBe(true);
    expect(fieldset.querySelector('#devtools-sentry-warning')?.textContent).toContain('send real error reports');

    buttons[1].click();

    expect(backend).toHaveBeenCalledTimes(1);
    expect(toast).toHaveBeenCalledWith('Sending backend crash request...', 'info', 'Sentry Test');
  });
});
