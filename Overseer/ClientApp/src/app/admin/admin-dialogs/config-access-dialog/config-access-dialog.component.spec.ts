import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AdminService, SystemAiConfigDto, UserSystemAiConfigDto } from '../../../services/admin.service';
import { AdminPageStore } from '../../admin-page.store';
import { buildSystemConfig, configureAdminTestBed } from '../../admin.component.testing';
import { ConfigAccessAssignment, ConfigAccessDialogComponent } from './config-access-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

/** An assignment of configuration `configId` with every limit unset. */
function buildAssignment(id: number, configId: number, overrides: Partial<UserSystemAiConfigDto> = {}): UserSystemAiConfigDto {
  return {
    id,
    systemAiApiConfigurationId: configId,
    isEnabled: true,
    orderIndex: 0,
    maxDailyChatRequests: null,
    maxMonthlyChatRequests: null,
    maxTotalChatRequests: null,
    dailyChatRequestsCount: 0,
    monthlyChatRequestsCount: 0,
    totalChatRequestsCount: 0,
    maxDailyTitleRequests: null,
    maxMonthlyTitleRequests: null,
    maxTotalTitleRequests: null,
    dailyTitleRequestsCount: 0,
    monthlyTitleRequestsCount: 0,
    totalTitleRequestsCount: 0,
    maxDailyChatTokens: null,
    maxMonthlyChatTokens: null,
    maxTotalChatTokens: null,
    dailyChatTokensCount: 0,
    monthlyChatTokensCount: 0,
    totalChatTokensCount: 0,
    maxDailyTitleTokens: null,
    maxMonthlyTitleTokens: null,
    maxTotalTitleTokens: null,
    dailyTitleTokensCount: 0,
    monthlyTitleTokensCount: 0,
    totalTitleTokensCount: 0,
    modelRole: 1,
    ...overrides
  };
}

/**
 * Three configurations: #1 Claude (high thinking, assigned but off and limited), #2 the system-wide
 * Gemini, #3 a disabled GPT.
 */
function threeConfigs(): SystemAiConfigDto[] {
  return [
    buildSystemConfig({ id: 1, displayName: 'Claude Chat', provider: 'Anthropic', modelId: 'claude-x', thinkingLevel: 'high', modelRole: 3, note: 'For testers' }),
    buildSystemConfig({ id: 2, displayName: 'Gemini Wide', provider: 'Google', modelId: 'gemini-y', isSystemWide: true, serviceTier: 'flex' }),
    buildSystemConfig({ id: 3, displayName: 'GPT Off', provider: 'OpenAI', modelId: 'gpt-z', isEnabled: false })
  ];
}

describe('ConfigAccessDialogComponent', () => {
  let fixture: ComponentFixture<ConfigAccessDialogComponent>;
  let component: ConfigAccessDialogComponent;
  let el: HTMLElement;
  let adminService: AdminService;
  let store: AdminPageStore;

  beforeEach(async () => {
    ({ adminService, store } = await configureAdminTestBed(ConfigAccessDialogComponent));
    store.configs = threeConfigs();
    vi.spyOn(adminService, 'getUserSystemConfigs').mockReturnValue(of([
      buildAssignment(11, 1, { isEnabled: false, maxDailyChatRequests: 100 })
    ]));
    vi.spyOn(adminService, 'getGroupSystemConfigs').mockReturnValue(of([buildAssignment(21, 2)]));
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      fixture.destroy();
    }
  });

  function create(): void {
    fixture = TestBed.createComponent(ConfigAccessDialogComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  function openForZed(): void {
    create();
    component.open({ kind: 'user', id: 'u-9', name: 'zed' });
    fixture.detectChanges();
  }

  const dialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>(':scope > dialog.config-access-dialog')!;
  const card = (id: number): HTMLElement => el.querySelector<HTMLElement>(`article.cad-card[data-config-id="${id}"]`)!;
  const toggleOf = (id: number): HTMLInputElement => card(id).querySelector<HTMLInputElement>('input.cad-toggle')!;
  const cardIds = (): string[] => Array.from(el.querySelectorAll('article.cad-card')).map(c => c.getAttribute('data-config-id')!);
  const status = (): string => textOf(el.querySelector('#cad-status'));

  it('opens for a user with its heading focused and loads the assignments', () => {
    openForZed();

    expect(dialog().open).toBe(true);
    const heading = dialog().querySelector('h3#config-access-title')!;
    expect(textOf(heading)).toBe('AI configuration access for zed');
    expect(document.activeElement).toBe(heading);
    expect(dialog().getAttribute('aria-labelledby')).toBe('config-access-title');
    expect(textOf(dialog().querySelector('.dialog-subtitle'))).toContain('Choose which system AI configurations zed can use');
    expect(dialog().hasAttribute('closedby')).toBe(false);
    expect(adminService.getUserSystemConfigs).toHaveBeenCalledWith('u-9');
  });

  it('shows one full-width card per configuration with its badges and the assignment state', () => {
    openForZed();

    const list = el.querySelector('ul.cad-cards')!;
    expect(list.getAttribute('role')).toBe('list');
    expect(list.getAttribute('aria-labelledby')).toBe('config-access-title');
    expect(cardIds()).toEqual(['1', '2', '3']);
    expect(list.querySelectorAll(':scope > li > article').length).toBe(3);

    const claude = card(1);
    expect(claude.getAttribute('aria-labelledby')).toBe('cad-1-title');
    expect(textOf(claude.querySelector('h4.cad-title'))).toBe('Claude Chat');
    expect(toggleOf(1).getAttribute('aria-labelledby')).toBe('cad-1-title');
    expect(toggleOf(1).checked).toBe(true);
    expect(textOf(claude.querySelector('.thinking-badge'))).toContain('High');
    expect(claude.querySelector('app-provider-badge')).not.toBeNull();
    expect(Array.from(claude.querySelectorAll('.status-badge.badge-role-chat, .status-badge.badge-role-title')).length).toBe(2);
    expect(textOf(claude.querySelector('.cad-off'))).toBe('Off for this user');
    expect(textOf(claude.querySelector('.cad-limited'))).toBe('Limited');
    expect(textOf(el.querySelector('#cad-1-limits-tip'))).toBe('Daily chat requests: 100');
    expect(claude.querySelector('.cad-model-id')).toBeNull();
    expect(claude.textContent).not.toContain('claude-x');
    expect(textOf(claude.querySelector('.cad-note'))).toBe('For testers');

    expect(textOf(card(2).querySelector('.badge-system-wide'))).toBe('System Wide');
    expect(textOf(card(2).querySelector('.config-badge'))).toContain('Flex');
    expect(textOf(card(3).querySelector('.badge-disabled'))).toBe('Disabled');
    expect(card(2).querySelector('.cad-meta')).toBeNull();
    expect(card(2).querySelector('.cad-off')).toBeNull();
    expect(card(2).querySelector('.cad-limited')).toBeNull();
  });

  it('shows the actions group only for an assigned configuration', () => {
    openForZed();

    const actions = card(1).querySelector('.cad-actions')!;
    expect(actions.getAttribute('role')).toBe('group');
    expect(actions.getAttribute('aria-label')).toBe('Actions for Claude Chat');
    expect(actions.querySelector('.cad-rate-limits')!.getAttribute('aria-label')).toBe('Rate limits of Claude Chat');
    expect(actions.querySelector('.cad-overrides')!.getAttribute('aria-label')).toBe('Overrides of Claude Chat');
    expect(card(2).querySelector('.cad-actions')).toBeNull();
    expect(card(3).querySelector('.cad-actions')).toBeNull();
  });

  it('counts the assignments in the status line', () => {
    openForZed();

    expect(el.querySelector('#cad-status')!.getAttribute('role')).toBe('status');
    expect(status()).toBe('1 of 3 configurations assigned');
  });

  it('assigns a configuration when its switch is turned on', () => {
    const create$ = vi.spyOn(adminService, 'createUserSystemConfig').mockReturnValue(of(buildAssignment(12, 2)));
    openForZed();

    toggleOf(2).click();
    fixture.detectChanges();

    expect(create$).toHaveBeenCalledWith('u-9', expect.objectContaining({
      systemAiApiConfigurationId: 2, isEnabled: true, modelRole: 1, maxDailyChatRequests: null
    }));
    expect(toggleOf(2).checked).toBe(true);
    expect(card(2).querySelector('.cad-actions')).not.toBeNull();
    expect(status()).toBe('2 of 3 configurations assigned');
  });

  it('removes an assignment when its switch is turned off', () => {
    const delete$ = vi.spyOn(adminService, 'deleteUserSystemConfig').mockReturnValue(of(undefined));
    openForZed();

    toggleOf(1).click();
    fixture.detectChanges();

    expect(delete$).toHaveBeenCalledWith(11);
    expect(toggleOf(1).checked).toBe(false);
    expect(card(1).querySelector('.cad-actions')).toBeNull();
    expect(status()).toBe('0 of 3 configurations assigned');
  });

  it('turns the switch back when the server refuses', () => {
    vi.spyOn(adminService, 'createUserSystemConfig').mockReturnValue(throwError(() => new Error('refused')));
    openForZed();

    toggleOf(3).click();
    fixture.detectChanges();

    expect(toggleOf(3).checked).toBe(false);
    expect(toggleOf(3).disabled).toBe(false);
    expect(card(3).querySelector('.cad-actions')).toBeNull();
    expect(status()).toBe('1 of 3 configurations assigned');
  });

  it('opens the rate limits above it, and closing them keeps this dialog open', async () => {
    openForZed();

    card(1).querySelector<HTMLButtonElement>('.cad-rate-limits')!.click();
    fixture.detectChanges();
    const limits = el.querySelector<HTMLDialogElement>('app-admin-rate-limits-dialog dialog')!;
    expect(limits.open).toBe(true);
    expect(component.rateLimitsDialog.limitContext).toBe('user');
    expect(component.rateLimitsDialog.limitEntityId).toBe(11);

    limits.close();
    await settle();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(component.subject?.name).toBe('zed');
  });

  it('applies a saved override to its card', () => {
    openForZed();

    component.onOverrideSaved({ ...component.assignmentOf(1)!, isEnabled: true } as ConfigAccessAssignment);
    fixture.detectChanges();

    expect(card(1).querySelector('.cad-off')).toBeNull();
  });

  it('shows the search field only above 8 configurations', () => {
    store.configs = Array.from({ length: 8 }, (_, i) => buildSystemConfig({ id: i + 1, displayName: `Config ${i + 1}` }));
    openForZed();
    expect(el.querySelector('#cad-search')).toBeNull();
  });

  it('filters by display name, model id and provider; Escape with text clears it and keeps the dialog open', async () => {
    store.configs = [
      ...threeConfigs(),
      ...Array.from({ length: 6 }, (_, i) => buildSystemConfig({ id: i + 10, displayName: `Extra ${i}`, provider: 'Anthropic', modelId: `extra-${i}` }))
    ];
    openForZed();
    const search = el.querySelector<HTMLInputElement>('#cad-search')!;
    expect(textOf(el.querySelector('label[for="cad-search"]'))).toBe('Search configurations');

    search.value = 'gemini';
    search.dispatchEvent(new Event('input'));
    await settle(250);
    fixture.detectChanges();
    expect(cardIds()).toEqual(['2']);
    expect(status()).toBe('1 of 9 configurations assigned · 1 shown');

    search.value = 'openai';
    search.dispatchEvent(new Event('input'));
    await settle(250);
    fixture.detectChanges();
    expect(cardIds()).toEqual(['3']);

    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    search.dispatchEvent(escape);
    fixture.detectChanges();
    expect(escape.defaultPrevented).toBe(true);
    expect(cardIds().length).toBe(9);
    expect(dialog().open).toBe(true);
  });

  it('closes on Escape and on Done, and forgets the subject', async () => {
    openForZed();

    // requestClose() is the path Escape takes: cancel, then close. A synthetic keydown would not close it.
    const native = dialog() as HTMLDialogElement & { requestClose?: () => void };
    if (typeof native.requestClose === 'function') native.requestClose();
    else native.close();
    await settle();
    fixture.detectChanges();
    expect(dialog().open).toBe(false);
    expect(component.subject).toBeNull();

    component.open({ kind: 'user', id: 'u-9', name: 'zed' });
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.cad-done')!.click();
    await settle();
    fixture.detectChanges();
    expect(dialog().open).toBe(false);
    expect(el.querySelector('.cad-done')!.classList).toContain('btn-gh-cancel');
  });

  it('shows the empty state when there are no configurations', () => {
    store.configs = [];
    openForZed();

    expect(textOf(el.querySelector('.cad-empty'))).toBe('No system configurations available.');
    expect(status()).toBe('');
  });

  it('opens for a group with the group heading and endpoints', () => {
    const create$ = vi.spyOn(adminService, 'createGroupSystemConfig').mockReturnValue(of(buildAssignment(22, 1)));
    const delete$ = vi.spyOn(adminService, 'deleteGroupSystemConfig').mockReturnValue(of(undefined));
    create();
    component.open({ kind: 'group', id: 5, name: 'Testers' });
    fixture.detectChanges();

    expect(textOf(dialog().querySelector('h3'))).toBe('AI configuration access for group Testers');
    expect(dialog().querySelector('.cad-model-id')).toBeNull();
    expect(dialog().textContent).not.toContain('claude-x');
    expect(adminService.getGroupSystemConfigs).toHaveBeenCalledWith(5);
    expect(toggleOf(2).checked).toBe(true);

    toggleOf(1).click();
    fixture.detectChanges();
    expect(create$).toHaveBeenCalledWith(5, expect.objectContaining({ systemAiApiConfigurationId: 1 }));

    toggleOf(2).click();
    fixture.detectChanges();
    expect(delete$).toHaveBeenCalledWith(21);
    expect(status()).toBe('1 of 3 configurations assigned');
  });

  it('shows Off for this group for a group assignment that is off', () => {
    vi.spyOn(adminService, 'getGroupSystemConfigs').mockReturnValue(of([buildAssignment(21, 2, { isEnabled: false })]));
    create();
    component.open({ kind: 'group', id: 5, name: 'Testers' });
    fixture.detectChanges();

    expect(textOf(card(2).querySelector('.cad-off'))).toBe('Off for this group');
  });
});
