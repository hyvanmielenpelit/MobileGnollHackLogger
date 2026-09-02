import {
  Component, ChangeDetectionStrategy, Input, Output, EventEmitter,
  OnChanges, SimpleChanges
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ConfigFilter, FilterChip, MatchMode, ROLE_OPTIONS,
  createEmptyFilter, activeFilterChips
} from './config-filter.model';

@Component({
  selector: 'app-config-filter',
  imports: [CommonModule],              // no FormsModule — nothing here uses ngModel
  templateUrl: './config-filter.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './config-filter.component.scss'
})
export class ConfigFilterComponent implements OnChanges {
  @Input({ required: true }) filter!: ConfigFilter;

  /** Bound to AdminComponent.adminProviders, so there is one source of truth. */
  @Input({ required: true }) providers!: string[];

  @Output() filterChange = new EventEmitter<ConfigFilter>();

  /** Template cannot reach module-level constants; re-expose as an instance member. */
  readonly roleOptions = ROLE_OPTIONS;

  panelOpen = false;

  // Recomputed in ngOnChanges, NOT getters. A getter returning a fresh array would
  // allocate on every change-detection pass under ChangeDetectionStrategy.Eager and
  // defeat @for diffing — the same reason AdminComponent.visibleConfigs is a field.
  chips: FilterChip[] = [];
  activeCount = 0;
  triggerAccessibleName = 'Filter configurations';

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['filter'] || changes['providers']) {
      this.recompute();
    }
  }

  private recompute(): void {
    this.chips = activeFilterChips(this.filter, this.providers);
    this.activeCount = this.filter.roles.length + this.filter.providers.length;
    this.triggerAccessibleName = this.activeCount === 0
      ? 'Filter configurations'
      : `Filter configurations, ${this.activeCount} ` +
        `${this.activeCount === 1 ? 'filter' : 'filters'} active`;
  }

  onPanelToggle(event: ToggleEvent): void {
    this.panelOpen = event.newState === 'open';
  }

  hasRole(bit: number): boolean {
    return this.filter.roles.includes(bit);
  }

  toggleRole(bit: number): void {
    const roles = this.hasRole(bit)
      ? this.filter.roles.filter(r => r !== bit)
      : [...this.filter.roles, bit];
    this.filterChange.emit({ ...this.filter, roles });
  }

  hasProvider(name: string): boolean {
    return this.filter.providers.includes(name);
  }

  toggleProvider(name: string): void {
    const providers = this.hasProvider(name)
      ? this.filter.providers.filter(p => p !== name)
      : [...this.filter.providers, name];
    this.filterChange.emit({ ...this.filter, providers });
  }

  setRoleMatchMode(mode: MatchMode): void {
    this.filterChange.emit({ ...this.filter, roleMatchMode: mode });
  }

  clearAll(): void {
    this.filterChange.emit(createEmptyFilter());
  }

  removeChip(chip: FilterChip): void {
    if (chip.kind === 'role') {
      this.filterChange.emit({
        ...this.filter,
        roles: this.filter.roles.filter(r => r !== chip.value)
      });
    } else {
      this.filterChange.emit({
        ...this.filter,
        providers: this.filter.providers.filter(p => p !== chip.value)
      });
    }
    // The clicked chip is about to be destroyed from the DOM.
    // Return focus to the trigger button to prevent it from dropping to the body.
    document.getElementById('config-filter-trigger')?.focus();
  }
}
