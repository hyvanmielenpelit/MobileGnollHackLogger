import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SimpleChange } from '@angular/core';
import { ConfigFilterComponent } from './config-filter.component';
import {
  ConfigFilter,
  ROLE_CHAT,
  ROLE_TITLE,
  ROLE_BENCHMARK,
  createEmptyFilter,
  isFilterActive,
  matchesFilter,
  activeFilterChips
} from './config-filter.model';

describe('ConfigFilterModel', () => {
  describe('createEmptyFilter and isFilterActive', () => {
    it('returns a fresh empty filter each time', () => {
      const f1 = createEmptyFilter();
      const f2 = createEmptyFilter();
      expect(f1).toEqual({ roles: [], roleMatchMode: 'any', providers: [] });
      expect(f1).not.toBe(f2);
      expect(f1.roles).not.toBe(f2.roles);
      expect(f1.providers).not.toBe(f2.providers);

      f1.roles.push(ROLE_CHAT);
      expect(f2.roles.length).toBe(0);
    });

    it('determines if filter is active', () => {
      const f = createEmptyFilter();
      expect(isFilterActive(f)).toBeFalse();
      f.roles = [ROLE_CHAT];
      expect(isFilterActive(f)).toBeTrue();
      f.roles = [];
      f.providers = ['OpenAI'];
      expect(isFilterActive(f)).toBeTrue();
    });
  });

  describe('matchesFilter semantics', () => {
    const chatConfig = { modelRole: ROLE_CHAT, provider: 'OpenAI' };
    const chatAndTitleConfig = { modelRole: ROLE_CHAT | ROLE_TITLE, provider: 'Google' };
    const titleOnlyAnthropic = { modelRole: ROLE_TITLE, provider: 'Anthropic' };
    const benchmarkGoogle = { modelRole: ROLE_BENCHMARK, provider: 'Google' };

    it('matches everything when filter is empty', () => {
      const f = createEmptyFilter();
      expect(matchesFilter(chatConfig, f)).toBeTrue();
      expect(matchesFilter(chatAndTitleConfig, f)).toBeTrue();
      expect(matchesFilter(titleOnlyAnthropic, f)).toBeTrue();
      expect(matchesFilter(benchmarkGoogle, f)).toBeTrue();
    });

    it('filters by role in "any" mode (OR)', () => {
      const f: ConfigFilter = { roles: [ROLE_CHAT, ROLE_BENCHMARK], roleMatchMode: 'any', providers: [] };
      expect(matchesFilter(chatConfig, f)).toBeTrue();
      expect(matchesFilter(chatAndTitleConfig, f)).toBeTrue();
      expect(matchesFilter(benchmarkGoogle, f)).toBeTrue();
      expect(matchesFilter(titleOnlyAnthropic, f)).toBeFalse();
    });

    it('filters by role in "all" mode (AND)', () => {
      const f: ConfigFilter = { roles: [ROLE_CHAT, ROLE_TITLE], roleMatchMode: 'all', providers: [] };
      expect(matchesFilter(chatConfig, f)).toBeFalse();
      expect(matchesFilter(titleOnlyAnthropic, f)).toBeFalse();
      expect(matchesFilter(chatAndTitleConfig, f)).toBeTrue();
    });

    it('differentiates any vs all for role filter [1, 4] with modelRole 3 (1 | 2)', () => {
      const config = { modelRole: ROLE_CHAT | ROLE_TITLE, provider: 'OpenAI' }; // 1 | 2 = 3
      const anyFilter: ConfigFilter = { roles: [ROLE_CHAT, ROLE_BENCHMARK], roleMatchMode: 'any', providers: [] };
      const allFilter: ConfigFilter = { roles: [ROLE_CHAT, ROLE_BENCHMARK], roleMatchMode: 'all', providers: [] };

      expect(matchesFilter(config, anyFilter)).toBeTrue(); // has Chat (1)
      expect(matchesFilter(config, allFilter)).toBeFalse(); // lacks Benchmark (4)
    });

    it('filters by provider in OR mode', () => {
      const f: ConfigFilter = { roles: [], roleMatchMode: 'any', providers: ['Anthropic', 'Google'] };
      expect(matchesFilter(titleOnlyAnthropic, f)).toBeTrue();
      expect(matchesFilter(benchmarkGoogle, f)).toBeTrue();
      expect(matchesFilter(chatConfig, f)).toBeFalse(); // OpenAI
    });

    it('combines role and provider facets using AND across facets', () => {
      const f: ConfigFilter = { roles: [ROLE_CHAT], roleMatchMode: 'any', providers: ['Google'] };
      expect(matchesFilter(chatConfig, f)).toBeFalse(); // Chat but OpenAI
      expect(matchesFilter(benchmarkGoogle, f)).toBeFalse(); // Google but Benchmark
      expect(matchesFilter(chatAndTitleConfig, f)).toBeTrue(); // Chat & Google
    });
  });

  describe('activeFilterChips', () => {
    it('returns role chips in display order followed by provider chips', () => {
      const f: ConfigFilter = {
        roles: [ROLE_BENCHMARK, ROLE_CHAT],
        roleMatchMode: 'any',
        providers: ['Google', 'Anthropic']
      };
      const providers = ['Anthropic', 'Google', 'OpenAI'];
      const chips = activeFilterChips(f, providers);

      expect(chips.length).toBe(4);
      expect(chips[0]).toEqual({
        kind: 'role',
        value: ROLE_CHAT,
        label: 'Chat',
        cssClass: 'badge-role-chat'
      });
      expect(chips[1]).toEqual({
        kind: 'role',
        value: ROLE_BENCHMARK,
        label: 'Benchmark',
        cssClass: 'badge-role-benchmark'
      });
      expect(chips[2]).toEqual({
        kind: 'provider',
        value: 'Anthropic',
        label: 'Anthropic',
        cssClass: ''
      });
      expect(chips[3]).toEqual({
        kind: 'provider',
        value: 'Google',
        label: 'Google',
        cssClass: ''
      });
    });
  });
});

describe('ConfigFilterComponent', () => {
  let component: ConfigFilterComponent;
  let fixture: ComponentFixture<ConfigFilterComponent>;
  const mockProviders = ['Anthropic', 'Google', 'OpenAI'];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ConfigFilterComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ConfigFilterComponent);
    component = fixture.componentInstance;
    component.filter = createEmptyFilter();
    component.providers = mockProviders;
    fixture.detectChanges();
  });

  it('renders trigger button with type="button", popovertarget, and aria-expanded="false" initially', () => {
    const trigger = fixture.nativeElement.querySelector('#config-filter-trigger');
    expect(trigger).toBeTruthy();
    expect(trigger.getAttribute('type')).toBe('button');
    expect(trigger.getAttribute('popovertarget')).toBe('config-filter-panel');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(trigger.getAttribute('aria-label')).toBe('Filter configurations');
    expect(trigger.textContent.trim()).toBe('');
  });

  it('updates trigger text and aria-label when filters are active', () => {
    const filter = { roles: [ROLE_CHAT], roleMatchMode: 'any' as const, providers: [] };
    component.filter = filter;
    component.ngOnChanges({
      filter: new SimpleChange(null, filter, false)
    });
    fixture.detectChanges();

    const trigger = fixture.nativeElement.querySelector('#config-filter-trigger');
    expect(trigger.textContent).toContain('1');
    expect(trigger.getAttribute('aria-label')).toBe('Filter configurations, 1 filter active');

    const multiFilter = { roles: [ROLE_CHAT, ROLE_TITLE], roleMatchMode: 'any' as const, providers: ['Google'] };
    component.filter = multiFilter;
    component.ngOnChanges({
      filter: new SimpleChange(filter, multiFilter, false)
    });
    fixture.detectChanges();

    expect(trigger.textContent).toContain('3');
    expect(trigger.getAttribute('aria-label')).toBe('Filter configurations, 3 filters active');
  });

  it('renders fieldsets with legends for each facet', () => {
    const fieldsets = fixture.nativeElement.querySelectorAll('fieldset.facet');
    expect(fieldsets.length).toBe(2);
    expect(fieldsets[0].querySelector('legend')?.textContent.trim()).toBe('Model Role');
    expect(fieldsets[1].querySelector('legend')?.textContent.trim()).toBe('Provider');
  });

  it('disables match-mode radios when fewer than 2 roles are selected', () => {
    const matchModeFieldset = fixture.nativeElement.querySelector('fieldset.match-mode');
    expect(matchModeFieldset.hasAttribute('disabled')).toBeTrue();

    const oneRole = { roles: [ROLE_CHAT], roleMatchMode: 'any' as const, providers: [] };
    component.filter = oneRole;
    component.ngOnChanges({ filter: new SimpleChange(null, oneRole, false) });
    fixture.detectChanges();
    expect(matchModeFieldset.hasAttribute('disabled')).toBeTrue();

    const twoRoles = { roles: [ROLE_CHAT, ROLE_TITLE], roleMatchMode: 'any' as const, providers: [] };
    component.filter = twoRoles;
    component.ngOnChanges({ filter: new SimpleChange(oneRole, twoRoles, false) });
    fixture.detectChanges();
    expect(matchModeFieldset.hasAttribute('disabled')).toBeFalse();
  });

  it('emits new filter on checkbox toggle without mutating input', () => {
    spyOn(component.filterChange, 'emit');
    const initialFilter = createEmptyFilter();
    component.filter = initialFilter;

    component.toggleRole(ROLE_CHAT);
    expect(component.filterChange.emit).toHaveBeenCalledWith({
      roles: [ROLE_CHAT],
      roleMatchMode: 'any',
      providers: []
    });
    expect(initialFilter.roles).toEqual([]); // No mutation

    component.toggleProvider('Google');
    expect(component.filterChange.emit).toHaveBeenCalledWith({
      roles: [],
      roleMatchMode: 'any',
      providers: ['Google']
    });
    expect(initialFilter.providers).toEqual([]); // No mutation
  });

  it('emits updated match mode when changed', () => {
    spyOn(component.filterChange, 'emit');
    component.filter = { roles: [ROLE_CHAT, ROLE_TITLE], roleMatchMode: 'any', providers: [] };
    component.setRoleMatchMode('all');
    expect(component.filterChange.emit).toHaveBeenCalledWith({
      roles: [ROLE_CHAT, ROLE_TITLE],
      roleMatchMode: 'all',
      providers: []
    });
  });

  it('recomputes chips on ngOnChanges and retains array reference across CD when unchanged', () => {
    const filter = { roles: [ROLE_CHAT], roleMatchMode: 'any' as const, providers: ['Anthropic'] };
    component.filter = filter;
    component.ngOnChanges({ filter: new SimpleChange(null, filter, false) });
    fixture.detectChanges();

    const chipsRef1 = component.chips;
    expect(chipsRef1.length).toBe(2);

    fixture.detectChanges();
    expect(component.chips).toBe(chipsRef1); // Same reference!
  });

  it('renders chips as buttons with aria-label and emits removed filter on click', () => {
    spyOn(component.filterChange, 'emit');
    const filter = { roles: [ROLE_CHAT], roleMatchMode: 'any' as const, providers: ['Anthropic'] };
    component.filter = filter;
    component.ngOnChanges({ filter: new SimpleChange(null, filter, false) });
    fixture.detectChanges();

    const chipButtons = fixture.nativeElement.querySelectorAll('.filter-chip:not(.filter-chip-static)');
    expect(chipButtons.length).toBe(2);
    expect(chipButtons[0].getAttribute('aria-label')).toBe('Remove filter: Role Chat');
    expect(chipButtons[1].getAttribute('aria-label')).toBe('Remove filter: Provider Anthropic');

    chipButtons[0].click();
    expect(component.filterChange.emit).toHaveBeenCalledWith({
      roles: [],
      roleMatchMode: 'any',
      providers: ['Anthropic']
    });
  });

  it('disables Clear All Filters when activeCount is 0, emits empty filter on click when active', () => {
    spyOn(component.filterChange, 'emit');
    const clearBtn = fixture.nativeElement.querySelector('.panel-actions button.btn-gh:not(.btn-gh-cancel)');
    expect(clearBtn.disabled).toBeTrue();

    const filter = { roles: [ROLE_CHAT], roleMatchMode: 'any' as const, providers: [] };
    component.filter = filter;
    component.ngOnChanges({ filter: new SimpleChange(null, filter, false) });
    fixture.detectChanges();

    expect(clearBtn.disabled).toBeFalse();
    clearBtn.click();
    expect(component.filterChange.emit).toHaveBeenCalledWith(createEmptyFilter());
  });

  it('does not have any title attributes in the template', () => {
    const elementsWithTitle = fixture.nativeElement.querySelectorAll('[title]');
    expect(elementsWithTitle.length).toBe(0);
  });
});
