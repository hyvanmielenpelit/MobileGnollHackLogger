import {
  CHAT_BOLD_FAMILY_PROPERTY, CHAT_BOLD_WEIGHTS, CHAT_BOLD_WEIGHT_STORAGE_KEY,
  applyChatBoldWeight, readChatBoldWeight, writeChatBoldWeight
} from './chat-bold-weight.util';

describe('chat bold weight', () => {
  const rootProperty = () => document.documentElement.style.getPropertyValue(CHAT_BOLD_FAMILY_PROPERTY);

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem(CHAT_BOLD_WEIGHT_STORAGE_KEY);
    document.documentElement.style.removeProperty(CHAT_BOLD_FAMILY_PROPERTY);
  });

  it('reads 800 when nothing is stored', () => {
    expect(readChatBoldWeight()).toBe(800);
  });

  it('round-trips each weight through write and read', () => {
    for (const weight of CHAT_BOLD_WEIGHTS) {
      writeChatBoldWeight(weight);
      expect(localStorage.getItem(CHAT_BOLD_WEIGHT_STORAGE_KEY)).toBe(String(weight));
      expect(readChatBoldWeight()).toBe(weight);
    }
  });

  it('reads anything but an exact weight as 800', () => {
    for (const stored of ['500', '650', 'abc', '1000', '600.0', ' 600', '6e2', '']) {
      localStorage.setItem(CHAT_BOLD_WEIGHT_STORAGE_KEY, stored);
      expect(readChatBoldWeight(), JSON.stringify(stored)).toBe(800);
    }
  });

  it('reads 800 when storage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied'); });

    expect(readChatBoldWeight()).toBe(800);
  });

  it('applies the weight when storing it throws', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('denied'); });

    writeChatBoldWeight(700);

    expect(rootProperty()).toBe('"Lato Chat Bold"');
  });

  it('sets the family on the document root, and removes it again for 800', () => {
    writeChatBoldWeight(900);
    expect(rootProperty()).toBe('"Lato Chat Black"');

    writeChatBoldWeight(800);
    expect(rootProperty()).toBe('');
  });

  it('applies the stored weight by default', () => {
    localStorage.setItem(CHAT_BOLD_WEIGHT_STORAGE_KEY, '600');

    applyChatBoldWeight();

    expect(rootProperty()).toBe('"Lato Chat Semibold"');
  });
});
