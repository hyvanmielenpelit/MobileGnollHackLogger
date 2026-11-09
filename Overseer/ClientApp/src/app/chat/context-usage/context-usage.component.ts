import { ChangeDetectionStrategy, ChangeDetectorRef, Component, Input, OnInit, inject } from '@angular/core';

import { ChatContextUsage } from '../../services/chat.service';
import { UserAiModel } from '../../services/settings.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import {
  ContextBreakdownRow, ContextView, LatestReplyView, contextBreakdownRows, contextView, formatCompactTokens, latestReplyView
} from './context-usage.util';

/**
 * The chat header's context read-out: a ring and a percentage on one button, with details in a breakdown
 * panel on click. `usage` is what the last reply measured; `selectedModel` decides whether that still
 * applies or has to be projected.
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

  /** The percentage, or a compact token count when the window is unknown. */
  get buttonText(): string {
    return this.percentText || formatCompactTokens(this.view.usedTokens);
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
    const replyCut = this.reply.reachedLimit ? ', latest reply reached its output limit' : '';
    if (view.windowTokens === null) return `Context usage: ${used} tokens${replyCut}. Show breakdown`;
    const about = view.state === 'projected' ? 'about ' : '';
    const nearlyFull = view.severity === 'critical' ? 'nearly full, ' : '';
    return `Context usage: ${about}${view.percent}%, ${nearlyFull}${used} of ${view.windowTokens.toLocaleString()} tokens${replyCut}. Show breakdown`;
  }

  /** What the header's warning glyph says; empty when it is not shown. */
  get warningText(): string {
    if (this.view.severity === 'critical') return 'nearly full';
    return this.reply.reachedLimit ? 'reply reached its output limit' : '';
  }

  get reply(): LatestReplyView {
    return latestReplyView(this.usage, this.selectedModel);
  }

  get replyTokensText(): string {
    const reply = this.reply;
    const output = reply.outputTokens.toLocaleString();
    const tokens = reply.limitTokens === null
      ? `${output} tokens`
      : `${output} of ${reply.limitTokens.toLocaleString()} tokens`;
    return reply.reasoningTokens > 0
      ? `${tokens}, ${reply.reasoningTokens.toLocaleString()} of them reasoning`
      : tokens;
  }

  get replyBarLabel(): string {
    const reply = this.reply;
    const parts: [string, number][] = [
      ['reasoning', reply.reasoningTokens],
      ['answer', reply.outputTokens - reply.reasoningTokens],
      ['unused', Math.max(0, (reply.limitTokens ?? 0) - reply.outputTokens)]
    ];
    const text = parts
      .filter(([, tokens]) => tokens > 0)
      .map(([label, tokens]) => `${label} ${tokens.toLocaleString()} tokens`)
      .join(', ');
    return text.charAt(0).toUpperCase() + text.slice(1);
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
      refreshAnchorPositioning();
    }
    this.cdr.markForCheck();
  }
}
