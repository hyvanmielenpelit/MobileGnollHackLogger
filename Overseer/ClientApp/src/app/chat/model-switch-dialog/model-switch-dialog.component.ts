import { ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, EventEmitter, Output, ViewChild, inject } from '@angular/core';

import { ModelSwitchImpact } from '../context-usage/context-usage.util';

export interface ModelSwitchDialogData {
  currentName: string;
  targetName: string;
  impact: ModelSwitchImpact;
  /** The chat's measured tokens; null in a chat with no measurement. */
  usedTokens: number | null;
  /** The models share a provider whose cache scope is not established, so the text says "may". */
  cacheMayCarryOver: boolean;
  isStreaming: boolean;
}

export interface ModelSwitchDecision {
  switchModel: boolean;
  dontWarnAgain: boolean;
}

/**
 * Asks before a model change that costs something. Every way out but **Switch Model** keeps the current
 * model. "Don't warn me again" is offered only when the cache alone is affected.
 */
@Component({
  selector: 'app-model-switch-dialog',
  standalone: true,
  templateUrl: './model-switch-dialog.component.html',
  styleUrls: ['./model-switch-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelSwitchDialogComponent {
  private readonly cdr = inject(ChangeDetectorRef);

  @Output() readonly decided = new EventEmitter<ModelSwitchDecision>();

  @ViewChild('dialog') dialog?: ElementRef<HTMLDialogElement>;

  data: ModelSwitchDialogData | null = null;
  dontWarnAgain = false;

  private decision: ModelSwitchDecision | null = null;

  open(data: ModelSwitchDialogData): void {
    this.data = data;
    this.dontWarnAgain = false;
    this.decision = null;
    this.cdr.detectChanges();
    this.dialog?.nativeElement.showModal();
  }

  get hasCacheEffect(): boolean {
    return this.data?.impact.cache !== 'none';
  }

  get cacheIsOnlyEffect(): boolean {
    return this.hasCacheEffect && !this.data?.impact.truncates;
  }

  get cacheCertain(): boolean {
    return this.data?.impact.cache === 'cold' && !this.data.cacheMayCarryOver;
  }

  onDontWarnChange(event: Event): void {
    this.dontWarnAgain = (event.target as HTMLInputElement).checked;
  }

  choose(switchModel: boolean): void {
    this.decision = { switchModel, dontWarnAgain: this.cacheIsOnlyEffect && this.dontWarnAgain };
    this.dialog?.nativeElement.close();
  }

  onClose(): void {
    this.decided.emit(this.decision ?? { switchModel: false, dontWarnAgain: false });
    this.decision = null;
  }

  /** Closes on a backdrop click where `closedby` is unsupported. */
  onDialogClick(event: MouseEvent): void {
    if (event.target === this.dialog?.nativeElement) {
      this.dialog.nativeElement.close();
    }
  }

  formatTokens(tokens: number): string {
    return tokens.toLocaleString();
  }
}
