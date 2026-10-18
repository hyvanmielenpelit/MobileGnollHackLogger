import { ErrorHandler } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { AdminDevtoolsTabComponent } from './admin-devtools-tab.component';
import { AdminService } from '../../services/admin.service';
import { AdminDevtoolsService } from '../../services/admin-devtools.service';
import { AdminPageStore } from '../admin-page.store';
import { adminTestRoutes, configureAdminTestBed } from '../admin.component.testing';

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

  it('keeps the heading and the sub-tab row together in one sticky head over an opaque ground', () => {
    fixture.detectChanges();

    const head = host().querySelector('.devtools-head') as HTMLElement;
    expect(head.querySelector('.tab-header-row')).not.toBeNull();
    const list = head.querySelector('[role="tablist"]') as HTMLElement;
    expect(list).not.toBeNull();
    const style = getComputedStyle(head);
    expect(style.position).toBe('sticky');
    expect(style.top).toBe('0px');
    expect(getComputedStyle(list).backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
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

  describe('Sentry Crash Reporting panel', () => {
    const sentryButton = (label: string) =>
      Array.from(host().querySelectorAll('#devtools-subpanel-sentry .btn-gh'))
        .find(b => b.textContent?.trim() === label) as HTMLButtonElement;
    const resultLine = (kind: string) => host().querySelector(`#devtools-sentry-${kind}-result`) as HTMLElement;

    function openSentry(): void {
      store.devtoolsSubTab = 'sentry';
      fixture.detectChanges();
    }

    it('renders the Sentry panel: warning banner, card, facts, two tests and the Configuration Health link', () => {
      openSentry();

      const panel = host().querySelector('#devtools-subpanel-sentry') as HTMLElement;
      const banner = panel.querySelector('#devtools-sentry-warning') as HTMLElement;
      expect(banner.classList).toContain('gh-dash-banner');
      expect(banner.getAttribute('data-status')).toBe('warning');
      expect(banner.textContent).toContain('real, unhandled exceptions');

      expect(panel.querySelector('h4#devtools-sentry-title')?.textContent?.trim()).toBe('Sentry Crash Reporting');
      const tests = Array.from(panel.querySelectorAll('article.devtools-sentry-test')) as HTMLElement[];
      expect(tests.map(t => t.querySelector('h5')?.textContent?.trim())).toEqual(['Frontend error', 'Backend error']);
      expect(tests.map(t => t.getAttribute('aria-labelledby')))
        .toEqual(['devtools-sentry-frontend-title', 'devtools-sentry-backend-title']);

      const buttons = Array.from(panel.querySelectorAll('.devtools-sentry-test .btn-gh')) as HTMLButtonElement[];
      expect(buttons.map(b => b.textContent?.trim())).toEqual(['Test Frontend Error', 'Test Backend Error']);
      expect(buttons.every(b => b.getAttribute('type') === 'button')).toBe(true);
      expect(buttons.map(b => b.getAttribute('aria-describedby'))).toEqual([
        'devtools-sentry-warning devtools-sentry-frontend-result',
        'devtools-sentry-warning devtools-sentry-backend-result'
      ]);
      expect(buttons.every(b => !b.hasAttribute('aria-disabled'))).toBe(true);
      for (const kind of ['frontend', 'backend']) {
        expect(resultLine(kind).getAttribute('role')).toBe('status');
        expect(resultLine(kind).textContent?.trim()).toBe('');
      }

      const link = panel.querySelector('.devtools-sentry-help a') as HTMLAnchorElement;
      expect(link.textContent?.trim()).toBe('Configuration Health');
      expect(link.getAttribute('href')).toBe('/admin/services/config-health');

      const facts = Array.from(panel.querySelectorAll('.devtools-sentry-facts dd')).map(d => d.textContent?.trim());
      expect(facts).toEqual(['Not initialized', 'Not initialized', 'Sent through /api/sentry/log']);
    });

    it('sizes the warning banner to its content, not to the 18rem flex basis', () => {
      openSentry();

      const banner = host().querySelector('#devtools-sentry-warning') as HTMLElement;
      expect(getComputedStyle(banner).flexGrow).toBe('0');
      // A banner that took the flex basis as its height was 288 px (18rem).
      expect(banner.getBoundingClientRect().height).toBeLessThan(160);
    });

    it('reports the backend test as sent when the server throws, without a toast', () => {
      const backend = vi.spyOn(adminService, 'triggerBackendSentryError')
        .mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      const toast = vi.spyOn(store, 'showToast');
      openSentry();

      sentryButton('Test Backend Error').click();
      fixture.detectChanges();

      expect(backend).toHaveBeenCalledTimes(1);
      const line = resultLine('backend');
      expect(line.querySelector('.gh-health-status')?.getAttribute('data-status')).toBe('ok');
      expect(line.textContent).toContain('Sent');
      expect(line.textContent).toContain('HTTP 500');
      expect(toast).not.toHaveBeenCalled();
    });

    it('keeps the backend button unavailable while the request is in flight', () => {
      const pending = new Subject<unknown>();
      const backend = vi.spyOn(adminService, 'triggerBackendSentryError').mockReturnValue(pending);
      openSentry();

      sentryButton('Test Backend Error').click();
      fixture.detectChanges();

      expect(sentryButton('Test Backend Error').getAttribute('aria-disabled')).toBe('true');
      expect(resultLine('backend').querySelector('app-loading-indicator')).toBeTruthy();

      sentryButton('Test Backend Error').click();
      fixture.detectChanges();
      expect(backend).toHaveBeenCalledTimes(1);
      pending.complete();
    });

    it('reports an answer without a throw as unexpected', () => {
      vi.spyOn(adminService, 'triggerBackendSentryError').mockReturnValue(of({}));
      openSentry();

      sentryButton('Test Backend Error').click();
      fixture.detectChanges();

      const line = resultLine('backend');
      expect(line.querySelector('.gh-health-status')?.getAttribute('data-status')).toBe('warning');
      expect(line.textContent).toContain('Unexpected');
      expect(line.textContent).toContain('Nothing was reported');
      expect(sentryButton('Test Backend Error').hasAttribute('aria-disabled')).toBe(false);
    });

    it('reports an unreachable server as failed', () => {
      vi.spyOn(adminService, 'triggerBackendSentryError')
        .mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
      openSentry();

      sentryButton('Test Backend Error').click();
      fixture.detectChanges();

      const line = resultLine('backend');
      expect(line.querySelector('.gh-health-status')?.getAttribute('data-status')).toBe('error');
      expect(line.textContent).toContain('Failed');
      expect(line.textContent).toContain('no response');
    });

    it('throws the frontend test error to the error handler and reports it as sent', async () => {
      // TestBed rethrows application errors outside the click; this run hands them to a spy instead.
      fixture.destroy();
      TestBed.resetTestingModule();
      const handleError = vi.fn();
      await TestBed.configureTestingModule({
        imports: [AdminDevtoolsTabComponent],
        providers: [
          provideRouter(adminTestRoutes()),
          provideHttpClient(),
          provideHttpClientTesting(),
          AdminPageStore,
          { provide: ErrorHandler, useValue: { handleError } }
        ],
        rethrowApplicationErrors: false
      }).compileComponents();
      store = TestBed.inject(AdminPageStore);
      fixture = TestBed.createComponent(AdminDevtoolsTabComponent);
      openSentry();

      sentryButton('Test Frontend Error').click();
      fixture.detectChanges();

      expect(handleError).toHaveBeenCalledTimes(1);
      expect(handleError.mock.calls[0][0]).toBeInstanceOf(Error);
      expect((handleError.mock.calls[0][0] as Error).message).toBe('Sentry Frontend Crash Test triggered by Admin');
      const line = resultLine('frontend');
      expect(line.querySelector('.gh-health-status')?.getAttribute('data-status')).toBe('ok');
      expect(line.textContent).toContain('Sent');
      expect(line.textContent).toContain('Thrown at');
    });
  });
});
