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

/** A one-line read-out beside the selection count, about the chosen items. */
export interface MultiPickerStatus {
  tone: 'busy' | 'ok' | 'warning' | 'error';
  text: string;
}

/**
 * What a key in a popup's search field asks the listbox to do. `commit` chooses the active option
 * (the multi-picker toggles it and stays open); `extendNext` / `extendPrev` move and add to the
 * selection where the picker selects several.
 */
export type MultiPickerNavigateIntent =
  'next' | 'prev' | 'pageNext' | 'pagePrev' | 'first' | 'last' | 'commit' | 'close' | 'extendNext' | 'extendPrev';

/** The context of a host's `toolsTemplate`, rendered at the top of the popup, outside the listbox. */
export interface MultiPickerToolsContext {
  listboxId: string;
  activeOptionId: string | null;
  navigate: (intent: MultiPickerNavigateIntent) => void;
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
