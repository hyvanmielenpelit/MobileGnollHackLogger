import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminConfigsTabComponent } from './admin-configs-tab.component';
import { AdminService, SystemAiConfigDto, SystemConfigDeletionCheckDto, SystemConfigBlockerDto, DefaultApiKeyStatus } from '../../services/admin.service';
import { createEmptyFilter } from '../config-filter/config-filter.model';
import { AdminPageStore } from '../admin-page.store';
import { buildSystemConfig, configureAdminTestBed } from '../admin.component.testing';
import { By } from '@angular/platform-browser';
import { ModelResolutionDialogComponent } from '../../shared/model-resolution-dialog/model-resolution-dialog.component';
import { ModelAvailability, ModelResolutionResult } from '../../shared/model-availability/model-availability';

describe('AdminConfigsTabComponent', () => {
  let component: AdminConfigsTabComponent;
  let fixture: ComponentFixture<AdminConfigsTabComponent>;
  let adminService: AdminService;
  let store: AdminPageStore;

  beforeEach(async () => {
    ({ adminService, store } = await configureAdminTestBed(AdminConfigsTabComponent));
    fixture = TestBed.createComponent(AdminConfigsTabComponent);
    component = fixture.componentInstance;
  });

  describe('System AI Configurations Filtering and Reordering', () => {
    const createMockConfig = (id: number, displayName: string, provider: string, modelRole: number): SystemAiConfigDto => buildSystemConfig({
      id,
      displayName,
      provider,
      modelId: displayName.toLowerCase().replace(/\s+/g, '-'),
      orderIndex: id,
      modelRole,
      parallelExecutionMode: 0,
      note: null
    });

    it('visibleConfigs equals configs with an empty filter', () => {
      const configs = [
        createMockConfig(1, 'Config 1', 'OpenAI', 1),
        createMockConfig(2, 'Config 2', 'Google', 2)
      ];
      component.configs = configs;
      component.configFilter = createEmptyFilter();
      component.applyConfigFilters();
      expect(component.visibleConfigs).toEqual(configs);
    });

    it('visibleConfigs narrows correctly when configFilter is set and applyConfigFilters runs', () => {
      const configs = [
        createMockConfig(1, 'Config 1', 'OpenAI', 1),
        createMockConfig(2, 'Config 2', 'Google', 2),
        createMockConfig(3, 'Config 3', 'Anthropic', 1)
      ];
      component.configs = configs;
      component.configFilter = { roles: [1], roleMatchMode: 'any', providers: ['OpenAI'] };
      component.applyConfigFilters();
      expect(component.visibleConfigs.length).toBe(1);
      expect(component.visibleConfigs[0].id).toBe(1);
    });

    it('dragging inside a filtered view reorders the right configs in the full array', () => {
      vi.spyOn(adminService, 'reorderSystemConfigs').mockReturnValue(of(true as any));
      // 5 configs: A (Chat), B (Title), C (Chat), D (Title), E (Chat)
      const c1 = createMockConfig(1, 'A', 'OpenAI', 1);
      const c2 = createMockConfig(2, 'B', 'Google', 2);
      const c3 = createMockConfig(3, 'C', 'OpenAI', 1);
      const c4 = createMockConfig(4, 'D', 'Anthropic', 2);
      const c5 = createMockConfig(5, 'E', 'OpenAI', 1);

      component.configs = [c1, c2, c3, c4, c5];
      // Filter to Chat (role 1) -> visibleConfigs are [c1, c3, c5]
      component.configFilter = { roles: [1], roleMatchMode: 'any', providers: [] };
      component.applyConfigFilters();
      expect(component.visibleConfigs.map(c => c.id)).toEqual([1, 3, 5]);

      // Drop visible index 2 (c5) onto visible index 0 (c1) (before midpoint, after = false)
      const dropEvent = {
        preventDefault: () => {},
        clientY: 10,
        target: {
          closest: () => ({
            classList: { remove: () => {} },
            getBoundingClientRect: () => ({ top: 0, height: 40 })
          })
        },
        dataTransfer: {
          getData: () => '2'
        }
      } as any;

      component.onConfigDrop(dropEvent, 0);

      // c5 should now be before c1 in the global configs array: [c5, c1, c2, c3, c4]
      expect(component.configs.map(c => c.id)).toEqual([5, 1, 2, 3, 4]);
      expect(adminService.reorderSystemConfigs).toHaveBeenCalledWith([5, 1, 2, 3, 4]);
    });

    it('renders result count with role="status" and aria-live="polite", absent when configs is empty', () => {
      fixture.detectChanges();
      component.configs = [];
      component.visibleConfigs = [];
      fixture.detectChanges();

      let resultCount = fixture.nativeElement.querySelector('.results-count');
      expect(resultCount).toBeNull();

      component.configs = [createMockConfig(1, 'Config 1', 'OpenAI', 1)];
      component.visibleConfigs = [...component.configs];
      fixture.detectChanges();

      resultCount = fixture.nativeElement.querySelector('.results-count');
      expect(resultCount).toBeTruthy();
      expect(resultCount.getAttribute('role')).toBe('status');
      expect(resultCount.getAttribute('aria-live')).toBe('polite');
      expect(resultCount.textContent).toContain('1 configurations');
    });

    it('renders filtered empty state only when configs.length > 0 && visibleConfigs.length === 0', () => {
      fixture.detectChanges();
      component.configs = [createMockConfig(1, 'Config 1', 'OpenAI', 1)];
      component.visibleConfigs = [];
      fixture.detectChanges();

      const emptyMsg = fixture.nativeElement.querySelector('.empty-state-msg');
      expect(emptyMsg).toBeTruthy();
      expect(emptyMsg.textContent).toContain('No configurations match the current filters.');
      expect(emptyMsg.querySelector('button')).toBeTruthy();
    });

    it('restoreConfigFilter rejects malformed localStorage data and falls back to clean empty filter', () => {
      const storageKey = 'overseer_admin_config_filters';
      localStorage.setItem(storageKey, JSON.stringify({
        roles: ['banana'],
        providers: [{}],
        roleMatchMode: 'xyzzy'
      }));

      component.restoreConfigFilter();
      expect(component.configFilter.roles).toEqual([]);
      expect(component.configFilter.providers).toEqual([]);
      expect(component.configFilter.roleMatchMode).toBe('any');

      localStorage.removeItem(storageKey);
    });
  });

  describe('delete config dialog', () => {
    const baseConfig: SystemAiConfigDto = buildSystemConfig({
      id: 42, displayName: "Prod GPT-5", provider: 'openai', modelId: 'gpt-5',
      orderIndex: 0, isEnabled: true, hasApiKey: true,
      isSystemWide: true,
      modelRole: 1, parallelExecutionMode: 2
    });

    const blocker = (overrides: Partial<SystemConfigBlockerDto> = {}): SystemConfigBlockerDto => ({
      kind: 'run', id: '900', runId: 900,
      label: "Benchmark run #900 on suite 'Core'",
      roles: ['assessor', 'claim verifier'],
      startedAtUtc: '2026-09-20T10:00:00Z',
      ...overrides
    });

    const blockedCheck = (blockers: SystemConfigBlockerDto[] = [blocker()]): SystemConfigDeletionCheckDto => ({
      configId: 42, displayName: "Prod GPT-5", canDelete: false, blockers,
      benchmarkRunReferenceCount: 3, stoppedSeriesCount: 0, stoppedBatteryRunCount: 0,
      userAssignmentCount: 2, groupAssignmentCount: 1, confidentialTrustCount: 0
    });

    const deletableCheck = (overrides: Partial<SystemConfigDeletionCheckDto> = {}): SystemConfigDeletionCheckDto => ({
      configId: 42, displayName: "Prod GPT-5", canDelete: true, blockers: [],
      benchmarkRunReferenceCount: 5, stoppedSeriesCount: 2, stoppedBatteryRunCount: 0,
      userAssignmentCount: 3, groupAssignmentCount: 1, confidentialTrustCount: 4,
      ...overrides
    });

    const dialogEl = (): HTMLDialogElement => fixture.nativeElement.querySelector('dialog.delete-config-dialog');
    const rowDeleteButton = (): HTMLButtonElement =>
      fixture.nativeElement.querySelector(`button[aria-label="Delete config ${baseConfig.displayName}"]`);
    const footerButton = (text: string): HTMLButtonElement =>
      (Array.from(dialogEl().querySelectorAll('.dialog-actions button')) as HTMLButtonElement[])
        .find(b => (b.textContent ?? '').includes(text))!;

    let toastSpy: Mock;

    beforeEach(() => {
      store.setConfigs([{ ...baseConfig }]);
      fixture.detectChanges();
      toastSpy = vi.spyOn(store, 'showToast').mockReturnValue(undefined);
      vi.spyOn(component.deleteConfigDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.deleteConfigDialog.nativeElement, 'close').mockReturnValue(undefined);
    });

    it('renders the row delete button with a distinct aria-label, an interestfor tooltip, and no title', () => {
      const button = rowDeleteButton();
      expect(button.getAttribute('type')).toBe('button');
      expect(button.hasAttribute('title')).toBe(false);
      const tip = fixture.nativeElement.querySelector('#' + button.getAttribute('interestfor'));
      expect(tip?.getAttribute('popover')).toBe('hint');
    });

    it('checks first, then opens directly in the blocked state naming each blocker, its roles and its start time', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(blockedCheck()));

      rowDeleteButton().click();
      fixture.detectChanges();

      expect(component.deleteConfigDialog.nativeElement.showModal).toHaveBeenCalled();
      expect(dialogEl().querySelector('#delete-config-title')!.textContent)
        .toContain("'Prod GPT-5' can't be deleted right now");

      const reason = dialogEl().querySelector('#delete-config-desc')!;
      expect(reason.getAttribute('role')).toBeNull(); // Not a race switch: no live announcement needed.

      const item = dialogEl().querySelector('.delete-config-blocker-list li')!;
      expect(item.textContent).toContain("Benchmark run #900 on suite 'Core'");
      expect(item.textContent).toContain('as the assessor and the claim verifier');
      expect(item.textContent).toContain('2026-09-20');

      expect(item.querySelector('button')!.textContent).toContain('Open run #900');

      const deleteBtn = footerButton('Delete');
      expect(deleteBtn.getAttribute('aria-disabled')).toBe('true');
      expect(deleteBtn.getAttribute('aria-describedby')).toBe('delete-config-desc');
    });

    it('opens directly in the deletable state with the impact list', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(deletableCheck()));

      rowDeleteButton().click();
      fixture.detectChanges();

      expect(dialogEl().querySelector('#delete-config-title')!.textContent).toContain("Delete 'Prod GPT-5'?");
      const list = dialogEl().querySelector('#delete-config-desc')!;
      expect(list.textContent).toContain('5 runs are unaffected');
      expect(list.textContent).toContain('3 user and 1 group assignments will be removed');
      expect(list.textContent).toContain("4 users' confidentiality decisions");
      expect(list.textContent).toContain('2 stopped benchmark series');
      expect(list.textContent).toContain('Usage and error logs are kept');
    });

    it('omits the confidentiality and stopped-series lines when their counts are zero', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(deletableCheck({ confidentialTrustCount: 0, stoppedSeriesCount: 0 })));

      rowDeleteButton().click();
      fixture.detectChanges();

      const list = dialogEl().querySelector('#delete-config-desc')!;
      expect(list.textContent).not.toContain('confidentiality');
      expect(list.textContent).not.toContain('stopped benchmark series');
      expect(list.textContent).not.toContain('stopped battery run');
    });

    for (const [count, line] of [
      [3, '3 stopped battery runs name this configuration and can no longer be resumed.'],
      [1, '1 stopped battery run names this configuration and can no longer be resumed.']
    ] as const) {
      it(`names ${count} stopped battery run(s) that can no longer be resumed`, () => {
        vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(deletableCheck({ stoppedBatteryRunCount: count })));

        rowDeleteButton().click();
        fixture.detectChanges();

        expect(dialogEl().querySelector('#delete-config-desc')!.textContent).toContain(line);
      });
    }

    it('opens in the error state when the pre-check itself fails', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(throwError(() => ({ status: 500, error: 'Deletion check is down' })));

      rowDeleteButton().click();
      fixture.detectChanges();

      expect(component.deleteConfigDialog.nativeElement.showModal).toHaveBeenCalled();
      const error = dialogEl().querySelector('.error-message[role="alert"]');
      expect(error?.textContent).toContain('Deletion check is down');
    });

    it('Check Again re-runs the check and switches state in place', () => {
      const checkSpy = vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValueOnce(of(blockedCheck())).mockReturnValueOnce(of(deletableCheck()));

      rowDeleteButton().click();
      fixture.detectChanges();
      expect(dialogEl().querySelector('#delete-config-title')!.textContent).toContain("can't be deleted right now");

      footerButton('Check Again').click();
      fixture.detectChanges();

      expect(checkSpy).toHaveBeenCalledTimes(2);
      expect(dialogEl().querySelector('#delete-config-title')!.textContent).toContain("Delete 'Prod GPT-5'?");
      expect(component.deleteConfigDialog.nativeElement.showModal).toHaveBeenCalledTimes(1); // Still open; not re-shown.
    });

    it('closes the dialog, switches to the Benchmark tab and hands off the run id on "Open run #N"', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(blockedCheck()));
      const emitSpy = vi.spyOn(component.openBenchmarkRun, 'emit');

      rowDeleteButton().click();
      fixture.detectChanges();

      dialogEl().querySelector<HTMLButtonElement>('.delete-config-blocker-list button')!.click();
      fixture.detectChanges();

      expect(component.deleteConfigDialog.nativeElement.close).toHaveBeenCalled();
      expect(emitSpy).toHaveBeenCalledWith(900);
    });

    it('switches a deletable dialog to blocked with a live-announced reason on a 409 race', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(deletableCheck()));
      vi.spyOn(adminService, 'deleteSystemConfig').mockReturnValue(throwError(() => ({
        status: 409,
        error: { error: "'Prod GPT-5' is in use by a benchmark right now.", blockers: [blocker()] }
      })));

      rowDeleteButton().click();
      fixture.detectChanges();

      footerButton('Delete').click();
      fixture.detectChanges();

      expect(dialogEl().querySelector('#delete-config-title')!.textContent).toContain("can't be deleted right now");
      const reason = dialogEl().querySelector('#delete-config-desc')!;
      expect(reason.getAttribute('role')).toBe('alert');
      expect(component.deleteConfigDialog.nativeElement.close).not.toHaveBeenCalled();
    });

    it('shows an inline error and keeps the dialog open for any other delete failure', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(deletableCheck()));
      vi.spyOn(adminService, 'deleteSystemConfig').mockReturnValue(throwError(() => ({
        status: 500, error: 'Something else went wrong.'
      })));

      rowDeleteButton().click();
      fixture.detectChanges();

      footerButton('Delete').click();
      fixture.detectChanges();

      expect(dialogEl().querySelector('#delete-config-title')!.textContent).toContain("Delete 'Prod GPT-5'?");
      const error = dialogEl().querySelector('.error-message[role="alert"]');
      expect(error?.textContent).toContain('Something else went wrong.');
      expect(component.deleteConfigDialog.nativeElement.close).not.toHaveBeenCalled();
      expect(footerButton('Delete').getAttribute('aria-disabled')).toBeNull();
    });

    it('deletes on success: removes the row, closes the dialog and shows the kept-history toast', () => {
      vi.spyOn(adminService, 'getSystemConfigDeletionCheck').mockReturnValue(of(deletableCheck()));
      vi.spyOn(adminService, 'deleteSystemConfig').mockReturnValue(of(undefined));

      rowDeleteButton().click();
      fixture.detectChanges();

      footerButton('Delete').click();
      fixture.detectChanges();

      expect(component.configs.find(c => c.id === 42)).toBeUndefined();
      expect(component.deleteConfigDialog.nativeElement.close).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith(
        "Deleted 'Prod GPT-5'. Benchmark history and usage logs are kept.", 'success'
      );
    });
  });

  describe('default API keys', () => {
    const keyStatus = (provider: string, overrides: Partial<DefaultApiKeyStatus> = {}): DefaultApiKeyStatus => ({
      provider, hasKey: false, keyHint: null, updatedAtUtc: null,
      verification: { status: null, checkedAtUtc: null, message: null },
      usedBy: [],
      ...overrides
    });

    const config = (id: number, displayName: string, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto => buildSystemConfig({
      id, displayName, orderIndex: id,
      ...overrides
    });

    it('passes the configuration form each provider\'s default key, unverified only when Not verified', () => {
      (adminService.getDefaultApiKeys as Mock).mockReturnValue(of([
        keyStatus('Anthropic', { hasKey: true, keyHint: 'ab12', verification: { status: 'NotVerified', checkedAtUtc: null, message: 'No response' } }),
        keyStatus('Google'),
        keyStatus('OpenAI', { hasKey: true, keyHint: 'zz99' })
      ]));

      store.loadDefaultApiKeys();

      expect(store.defaultKeysForForm).toEqual({
        Anthropic: { hasKey: true, keyHint: 'ab12', verified: false },
        Google: { hasKey: false, keyHint: null, verified: true },
        OpenAI: { hasKey: true, keyHint: 'zz99', verified: true }
      });
    });

    it('labels each configuration\'s key: Default Key, Default Key Missing, Key Saved or No Key', () => {
      store.setConfigs([
        config(1, 'Uses Default', { useDefaultApiKey: true, hasApiKey: true }),
        config(2, 'Default Gone', { useDefaultApiKey: true, hasApiKey: false, isEnabled: false }),
        config(3, 'Own Key', { useDefaultApiKey: false, hasApiKey: true }),
        config(4, 'Nothing', { hasApiKey: false })
      ]);
      fixture.detectChanges();

      const badges = Array.from(fixture.nativeElement.querySelectorAll('.config-key-badge')) as HTMLElement[];
      expect(badges.map(b => b.textContent!.trim())).toEqual(['Default Key', 'Default Key Missing', 'Key Saved', 'No Key']);
      expect(badges[1].classList).toContain('badge-warning');
      expect(badges[0].classList).toContain('badge-success');
    });

    it('sends useDefaultApiKey with a saved configuration', () => {
      const create = vi.spyOn(adminService, 'createSystemConfig').mockReturnValue(of(config(5, 'New')));
      fixture.detectChanges();
      component.isNewConfig = true;
      component.editingConfig = { provider: 'Anthropic', isEnabled: true };
      vi.spyOn(component.configDialog.nativeElement, 'close').mockReturnValue(undefined);

      component.onConfigSave({
        displayName: 'New', displayNameMode: 'model_id', provider: 'Anthropic', modelId: 'claude-x',
        thinkingLevel: null, reasoningMode: null, reasoningSummary: null, serviceTier: null,
        maxInputTokens: null, maxOutputTokens: null, useDefaultApiKey: true
      });

      expect(vi.mocked(create).mock.lastCall![0].useDefaultApiKey).toBe(true);
      expect(vi.mocked(create).mock.lastCall![0].apiKey).toBeUndefined();
    });
  });

  describe('model availability', () => {
    const retired = (overrides: Partial<ModelAvailability> = {}): ModelAvailability => ({
      status: 'retired', needsAttention: true, retiredOn: '2026-09-30', note: null, catalogDisplayName: null, ...overrides
    });
    const notInCatalog = (): ModelAvailability => ({ status: 'notInCatalog', needsAttention: true });
    const available = (): ModelAvailability => ({ status: 'available', needsAttention: false });

    const config = (
      id: number, displayName: string, modelAvailability: ModelAvailability | undefined, overrides: Partial<SystemAiConfigDto> = {}
    ): SystemAiConfigDto => buildSystemConfig({ id, displayName, modelId: `model-${id}`, orderIndex: id, modelAvailability, ...overrides });

    const resolution = (model: SystemAiConfigDto): ModelResolutionResult => ({ changes: [], blockers: [], model });

    const el = (): HTMLElement => fixture.nativeElement;
    const banner = () => el().querySelector<HTMLElement>('.config-attention-banner');
    const bannerButtons = () => Array.from(el().querySelectorAll<HTMLButtonElement>('.config-attention-resolve'));
    const rows = () => Array.from(el().querySelectorAll<HTMLElement>('.model-item'));
    const rowResolve = (id: number) => el().querySelector<HTMLButtonElement>(`#config-resolve-${id}`);
    const dialog = (): ModelResolutionDialogComponent =>
      fixture.debugElement.query(By.directive(ModelResolutionDialogComponent)).componentInstance;

    let openSpy: Mock;

    beforeEach(() => {
      openSpy = vi.spyOn(ModelResolutionDialogComponent.prototype, 'open').mockReturnValue(undefined);
    });

    afterEach(() => {
      openSpy.mockRestore();
    });

    it('summarizes the configurations that need attention in a status banner, retired ones first', () => {
      store.setConfigs([config(1, 'Alpha', notInCatalog()), config(2, 'Beta', retired()), config(3, 'Gamma', available())]);
      fixture.detectChanges();

      const summary = banner()!;
      expect(summary.getAttribute('role')).toBe('status');
      expect(summary.classList).toContain('alert-warning');
      expect(summary.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(summary.querySelector('.alert-heading')!.textContent!.trim()).toBe('2 configurations need attention');
      expect(summary.textContent).toContain('Their models are not in the model catalog. Switch, keep or delete each one.');
      expect(bannerButtons().map(b => b.textContent!.trim())).toEqual(['Resolve "Beta"', 'Resolve "Alpha"']);
      expect(bannerButtons().every(b => b.classList.contains('btn-ghost') && b.getAttribute('type') === 'button')).toBe(true);
      expect(bannerButtons().every(b => b.getAttribute('aria-haspopup') === 'dialog')).toBe(true);
    });

    it('words the banner in the singular for one configuration, and flags a disabled one', () => {
      store.setConfigs([config(1, 'Alpha', retired(), { isEnabled: false }), config(2, 'Beta', available())]);
      fixture.detectChanges();

      expect(banner()!.querySelector('.alert-heading')!.textContent!.trim()).toBe('1 configuration needs attention');
      expect(banner()!.textContent).toContain('Its model is not in the model catalog.');
      expect(rowResolve(1)).not.toBeNull();
    });

    it('shows no banner, badge or notice when every model is in the catalog', () => {
      store.setConfigs([config(1, 'Alpha', available()), config(2, 'Beta', undefined)]);
      fixture.detectChanges();

      expect(banner()).toBeNull();
      expect(el().querySelector('.config-availability-badge')).toBeNull();
      expect(el().querySelector('app-model-availability-notice')).toBeNull();
    });

    it('flags a retired row with a Removed badge after the provider badge, a notice and a Resolve button', () => {
      store.setConfigs([config(1, 'Alpha', retired({ catalogDisplayName: 'Claude Old' }))]);
      fixture.detectChanges();

      const badge = rows()[0].querySelector<HTMLElement>('.config-availability-badge')!;
      expect(badge.classList).toContain('badge-availability');
      expect(badge.classList).toContain('badge-warning');
      expect(badge.textContent!.trim()).toBe('Removed');
      expect(badge.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(badge.hasAttribute('title')).toBe(false);
      expect(badge.previousElementSibling!.tagName.toLowerCase()).toBe('app-provider-badge');
      const tip = el().querySelector('#' + badge.getAttribute('interestfor'))!;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.textContent).toContain('Claude Old was removed from the model catalog on September 30, 2026.');

      const notice = rows()[0].querySelector('app-model-availability-notice .model-availability-notice')!;
      expect(notice).not.toBeNull();
      const resolve = rowResolve(1)!;
      expect(notice.contains(resolve)).toBe(true);
      expect(resolve.textContent!.trim()).toBe('Resolve…');
      expect(resolve.getAttribute('aria-label')).toBe('Resolve "Alpha"');
      expect(resolve.getAttribute('aria-haspopup')).toBe('dialog');
      expect(resolve.getAttribute('type')).toBe('button');
      expect(resolve.classList).toContain('btn-ghost');
    });

    it('badges a model outside the catalog as Not in catalog and a custom-mode row as Custom model', () => {
      store.setConfigs([
        config(1, 'Alpha', notInCatalog()),
        config(2, 'Beta', { status: 'custom', needsAttention: false }, { modelCatalogMode: 'custom' })
      ]);
      fixture.detectChanges();

      const first = rows()[0].querySelector<HTMLElement>('.config-availability-badge')!;
      expect(first.textContent!.trim()).toBe('Not in catalog');
      expect(first.classList).toContain('badge-info');
      expect(el().querySelector('#' + first.getAttribute('interestfor'))!.textContent)
        .toContain("model-1 isn't in Overseer's model catalog");

      const second = rows()[1].querySelector<HTMLElement>('.config-availability-badge')!;
      expect(second.textContent!.trim()).toBe('Custom model');
      expect(second.classList).toContain('badge-neutral');
      expect(rows()[1].querySelector('app-model-availability-notice')).toBeNull();
      expect(rowResolve(2)).toBeNull();
    });

    it('opens the system resolution dialog from the row and from the banner', () => {
      store.setConfigs([config(1, 'Alpha', retired(), { provider: 'Anthropic' })]);
      fixture.detectChanges();

      expect(dialog().kind).toBe('system');
      rowResolve(1)!.click();
      expect(openSpy).toHaveBeenCalledWith({
        id: 1, provider: 'Anthropic', modelId: 'model-1', displayName: 'Alpha', availability: retired()
      });

      bannerButtons()[0].click();
      expect(openSpy).toHaveBeenCalledTimes(2);
    });

    it('replaces the resolved configuration in a new list and names the new model in a toast', () => {
      const original = config(1, 'Alpha', retired());
      store.setConfigs([original, config(2, 'Beta', available())]);
      fixture.detectChanges();
      const toast = vi.spyOn(store, 'showToast').mockReturnValue(undefined);
      const before = store.configs;
      const updated = { ...original, modelId: 'claude-new', modelAvailability: available() };

      dialog().resolved.emit(resolution(updated));
      fixture.detectChanges();

      expect(store.configs).not.toBe(before);
      expect(store.configs[0]).toBe(updated);
      expect(store.configs[1].id).toBe(2);
      expect(toast).toHaveBeenCalledWith("'Alpha' now uses claude-new.", 'success');
      expect(banner()).toBeNull();
      expect(rowResolve(1)).toBeNull();
    });

    it('says a configuration kept as a custom model is now one', () => {
      const original = config(1, 'Alpha', notInCatalog());
      store.setConfigs([original]);
      fixture.detectChanges();
      const toast = vi.spyOn(store, 'showToast').mockReturnValue(undefined);

      dialog().resolved.emit(resolution({
        ...original, modelCatalogMode: 'custom', modelAvailability: { status: 'custom', needsAttention: false }
      }));

      expect(toast).toHaveBeenCalledWith("'Alpha' is now a custom model.", 'success');
    });

    it('moves focus to the next flagged row, then to the resolved row\'s title, once the dialog closes', () => {
      store.setConfigs([config(1, 'Alpha', retired()), config(2, 'Beta', notInCatalog()), config(3, 'Gamma', available())]);
      fixture.detectChanges();

      dialog().resolved.emit(resolution({ ...store.configs[0], modelId: 'new-1', modelAvailability: available() }));
      dialog().closed.emit();
      expect(document.activeElement).toBe(rowResolve(2));

      dialog().resolved.emit(resolution({ ...store.configs[1], modelId: 'new-2', modelAvailability: available() }));
      dialog().closed.emit();
      expect(document.activeElement).toBe(el().querySelector('#config-title-2'));
    });

    it('leaves focus alone when the dialog closes without a resolution', () => {
      store.setConfigs([config(1, 'Alpha', retired()), config(2, 'Beta', retired())]);
      fixture.detectChanges();
      rowResolve(1)!.focus();

      dialog().closed.emit();

      expect(document.activeElement).toBe(rowResolve(1));
    });

    it('hands Delete… over to the row\'s own delete flow', () => {
      store.setConfigs([config(1, 'Alpha', retired())]);
      fixture.detectChanges();
      const requestDelete = vi.spyOn(component, 'requestDeleteConfig').mockReturnValue(undefined);

      dialog().deleteRequested.emit(1);

      expect(requestDelete).toHaveBeenCalledWith(store.configs[0]);
    });

    it('opens the dialog for the pending configuration once the tab is ready, then clears it', () => {
      store.setConfigs([config(1, 'Alpha', retired()), config(2, 'Beta', notInCatalog())]);
      store.pendingResolveConfigId = 2;

      fixture.detectChanges();

      expect(openSpy).toHaveBeenCalledTimes(1);
      expect(openSpy.mock.lastCall![0]).toEqual(expect.objectContaining({ id: 2, displayName: 'Beta' }));
      expect(store.pendingResolveConfigId).toBeNull();
    });

    it('ignores and clears an unknown pending configuration', () => {
      store.setConfigs([config(1, 'Alpha', retired())]);
      store.pendingResolveConfigId = 99;

      fixture.detectChanges();

      expect(openSpy).not.toHaveBeenCalled();
      expect(store.pendingResolveConfigId).toBeNull();
    });

    it('opens the dialog for a configuration requested while the tab is showing', () => {
      store.setConfigs([config(1, 'Alpha', retired())]);
      fixture.detectChanges();
      expect(openSpy).not.toHaveBeenCalled();

      store.requestResolveConfig(1);

      expect(openSpy).toHaveBeenCalledWith(expect.objectContaining({ id: 1 }));
      expect(store.pendingResolveConfigId).toBeNull();
    });
  });
});
