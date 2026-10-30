import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { ChatTurnNotice, ChatTurnNoticeAction } from '../../services/chat.service';

const ACTION_LABELS: Partial<Record<ChatTurnNoticeAction, string>> = {
  try_again: 'Try again',
  continue: 'Continue',
  new_chat: 'New chat'
};

const FACT_LABELS: Record<string, string> = {
  limitValue: 'Limit',
  observedValue: 'Observed',
  iterationsUsed: 'Research rounds used',
  modelCallCount: 'AI calls, including sub-agents',
  toolCallCount: 'Tool calls, including sub-agents',
  totalDurationMs: 'Duration',
  httpStatus: 'HTTP status',
  providerErrorCode: 'Provider error code',
  providerFinishReason: 'Provider finish reason',
  assistantPhase: 'Answer phase',
  providerErrorBody: 'Provider response'
};

let nextTitleId = 0;

/**
 * Why a turn stopped, under its assistant message. The text comes from the server's catalog. The
 * host decides whether the action is offered and carries it out. The notice has no live role, so
 * loading a chat's history reads none aloud; the chat page announces a notice that arrives live.
 */
@Component({
  selector: 'app-turn-notice',
  standalone: true,
  templateUrl: './turn-notice.component.html',
  styleUrl: './turn-notice.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TurnNoticeComponent {
  @Input({ required: true }) notice!: ChatTurnNotice;
  @Input() actionAvailable = false;
  @Output() actionTriggered = new EventEmitter<void>();

  readonly titleId = `turn-notice-title-${nextTitleId++}`;

  get actionLabel(): string | null {
    return this.actionAvailable ? ACTION_LABELS[this.notice.action] ?? null : null;
  }

  get facts(): { key: string; label: string; value: string }[] {
    return Object.entries(this.notice.adminFacts ?? {}).map(([key, value]) => ({
      key,
      label: FACT_LABELS[key] ?? key,
      value: TurnNoticeComponent.formatFact(key, value)
    }));
  }

  private static formatFact(key: string, value: unknown): string {
    if (key === 'totalDurationMs' && typeof value === 'number') return `${(value / 1000).toFixed(1)} s`;
    return typeof value === 'object' ? JSON.stringify(value) : String(value);
  }
}
