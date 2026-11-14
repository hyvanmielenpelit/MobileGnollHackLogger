/* The weight of bold text in the assistant's chat replies (Settings > General), kept in this
   browser. The authored weight stays 700; the setting picks the family that draws it, through the
   --chat-bold-family custom property on the document root (styles.scss, fonts.scss). */

export const CHAT_BOLD_WEIGHT_STORAGE_KEY = 'overseer_chat_bold_weight';
export const CHAT_BOLD_FAMILY_PROPERTY = '--chat-bold-family';
export const CHAT_BOLD_WEIGHTS = [600, 700, 800, 900] as const;
export type ChatBoldWeight = typeof CHAT_BOLD_WEIGHTS[number];
export const DEFAULT_CHAT_BOLD_WEIGHT: ChatBoldWeight = 800;

/** `family` is the @font-face family that draws the weight; null is the body family, whose 700 is Heavy. */
export const CHAT_BOLD_WEIGHT_OPTIONS: ReadonlyArray<{ value: ChatBoldWeight; label: string; family: string | null }> = [
  { value: 600, label: '600 · Semibold', family: 'Lato Chat Semibold' },
  { value: 700, label: '700 · Bold', family: 'Lato Chat Bold' },
  { value: 800, label: '800 · Heavy (default)', family: null },
  { value: 900, label: '900 · Black', family: 'Lato Chat Black' }
];

export function readChatBoldWeight(): ChatBoldWeight {
  try {
    const stored = localStorage.getItem(CHAT_BOLD_WEIGHT_STORAGE_KEY);
    return CHAT_BOLD_WEIGHTS.find(w => String(w) === stored) ?? DEFAULT_CHAT_BOLD_WEIGHT;
  } catch {
    return DEFAULT_CHAT_BOLD_WEIGHT;
  }
}

export function applyChatBoldWeight(weight: ChatBoldWeight = readChatBoldWeight()): void {
  const family = CHAT_BOLD_WEIGHT_OPTIONS.find(o => o.value === weight)?.family ?? null;
  const style = document.documentElement.style;
  if (family) {
    style.setProperty(CHAT_BOLD_FAMILY_PROPERTY, `"${family}"`);
  } else {
    style.removeProperty(CHAT_BOLD_FAMILY_PROPERTY);
  }
}

export function writeChatBoldWeight(weight: ChatBoldWeight): void {
  try {
    localStorage.setItem(CHAT_BOLD_WEIGHT_STORAGE_KEY, String(weight));
  } catch {
    /* Storage unavailable: the choice lasts until the page is reloaded. */
  }
  applyChatBoldWeight(weight);
}
