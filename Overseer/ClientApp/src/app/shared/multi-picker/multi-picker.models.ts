export type MultiPickerKey = string | number;

export interface MultiPickerOption {
  /** Unique within one picker. */
  key: MultiPickerKey;
  /** The option's name: its accessible name, the chip text and what type-ahead matches. */
  label: string;
  /** A short chip before the label (`.model-option-tag`). */
  tag?: string;
  /** A muted second part after the label. */
  detail?: string;
  /** Consecutive options with the same group share one heading. */
  group?: string;
  /** Makes the option `aria-disabled`, with this reason shown under it. */
  disabledReason?: string;
}

/** The chosen keys, in option order. */
export interface MultiPickerSelection {
  keys: MultiPickerKey[];
}

/**
 * The context of a host's `optionTemplate`, which must render text only, or `chipTemplate`, which
 * renders non-interactive content: text or a list.
 */
export interface MultiPickerOptionContext {
  $implicit: MultiPickerOption;
  selected: boolean;
  /**
   * Chip templates only: the id to give the element that shows the option's `detail`, which the
   * remove button names as its description.
   */
  noteId?: string;
}
