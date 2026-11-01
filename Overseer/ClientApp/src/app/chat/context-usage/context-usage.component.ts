import { ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, Input, OnInit, ViewChild, inject } from '@angular/core';

import { ChatContextUsage } from '../../services/chat.service';
import { UserAiModel } from '../../services/settings.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { ContextBreakdownRow, ContextView, contextBreakdownRows, contextUsageTooltipLines, contextView } from './context-usage.util';

/**
 * The chat header's context read-out: a percentage and a ring on one button, with details in a hint on
 * hover or focus and in a breakdown panel on click. `usage` is what the last reply measured; `selectedModel`
 * decides whether that still applies or has to be projected.
 */
@Component({
  selector: 'app-context-usage',
  standalone: true,
  templateUrl: './context-usage.component.html',
  styleUrls: ['./context-usage.component.scss'],
  host: { class: 'context-window-indicator' },
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ContextUsageComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) usage!: ChatContextUsage;
  @Input() selectedModel: UserAiModel | null | undefined;

  @ViewChild('usageTip') usageTip?: ElementRef<HTMLElement>;

  /** The popover polyfill does not set `aria-expanded`. */
  panelOpen = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  get view(): ContextView {
    return contextView(this.usage, this.selectedModel);
  }

  /** `~` marks a projected figure. Empty when no percentage is known. */
  get percentText(): string {
    const view = this.view;
    if (view.percent === null) return '';
    return `${view.state === 'projected' ? '~' : ''}${view.percent}%`;
  }

  get tokensText(): string {
    const view = this.view;
    const used = view.usedTokens.toLocaleString();
    if (view.windowTokens === null) return `${used} tokens`;
    return `${view.state === 'projected' ? 'About ' : ''}${used} / ${view.windowTokens.toLocaleString()} tokens`;
  }

  get ariaLabel(): string {
    const view = this.view;
    const used = view.usedTokens.toLocaleString();
    if (view.windowTokens === null) return `Context usage: ${used} tokens. Show breakdown`;
    const about = view.state === 'projected' ? 'about ' : '';
    const nearlyFull = view.severity === 'critical' ? 'nearly full, ' : '';
    return `Context usage: ${about}${view.percent}%, ${nearlyFull}${used} of ${view.windowTokens.toLocaleString()} tokens. Show breakdown`;
  }

  get tooltipLines(): string[] {
    const lines = contextUsageTooltipLines(this.usage);
    const view = this.view;
    if (view.state === 'projected') {
      lines.push(`On ${view.modelName} it would use about ${view.usedTokens.toLocaleString()} of ${view.windowTokens!.toLocaleString()} tokens.`);
    } else if (view.state === 'unknownWindow') {
      lines.push(`The context window of ${view.modelName} is not known.`);
    }
    return lines;
  }

  get rows(): ContextBreakdownRow[] {
    return contextBreakdownRows(this.usage);
  }

  get barLabel(): string {
    return this.rows.map(r => `${r.label} ${r.percent}%`).join(', ');
  }

  get hasEstimatedRows(): boolean {
    return this.rows.some(r => r.estimated);
  }

  get measuredName(): string {
    return this.usage.modelDisplayName || this.usage.modelId || 'the previous model';
  }

  formatTokens(tokens: number): string {
    return tokens.toLocaleString();
  }

  onPanelToggle(event: Event): void {
    this.panelOpen = (event as ToggleEvent).newState === 'open';
    if (this.panelOpen) {
      this.hideHint();
      refreshAnchorPositioning();
    }
    this.cdr.markForCheck();
  }

  private hideHint(): void {
    try {
      this.usageTip?.nativeElement.hidePopover();
    } catch {
      /* Already closed. */
    }
  }
}
