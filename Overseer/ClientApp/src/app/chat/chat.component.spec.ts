import { ChatComponent } from './chat.component';

describe('ChatComponent.stripThoughts', () => {
  it('should return empty string for null, undefined, or empty input', () => {
    expect(ChatComponent.stripThoughts(null)).toBe('');
    expect(ChatComponent.stripThoughts(undefined)).toBe('');
    expect(ChatComponent.stripThoughts('')).toBe('');
    expect(ChatComponent.stripThoughts('   ')).toBe('');
  });

  it('should return plain text untouched when there are no thinking tags', () => {
    const input = 'This is a normal response without any thinking blocks.';
    expect(ChatComponent.stripThoughts(input)).toBe(input);
  });

  it('should remove a single thinking block', () => {
    const input = '<div class="ai-thought">\n\nThinking about the problem...\n\n</div>\n\nHere is the actual answer.';
    expect(ChatComponent.stripThoughts(input)).toBe('Here is the actual answer.');
  });

  it('should remove multiple consecutive thinking blocks and collapse gaps', () => {
    const input = `
<div class="ai-thought">

I’m checking the GnollHack weapon listings for the exact count of two-handed bludgeoning weapons.

</div>

<div class="ai-thought">

The wiki confirms the category, but not the full item count, so I’m checking the weapon definitions directly to avoid missing special or artifact-base weapons.

</div>

<div class="ai-thought">

I found the two-handed weapon entries; I’m narrowing them to the bludgeoning skill rather than counting all two-handed weapons.

</div>

There is **1** two-handed bludgeoning weapon in GnollHack: the **two-handed club**. It is classified as both \`ENCHTYPE_TWO_HANDED_MELEE_WEAPON\` and \`P_BLUDGEONING_WEAPON\` in \`src/objects.c\` around lines 746–750.
`;
    const expected = 'There is **1** two-handed bludgeoning weapon in GnollHack: the **two-handed club**. It is classified as both `ENCHTYPE_TWO_HANDED_MELEE_WEAPON` and `P_BLUDGEONING_WEAPON` in `src/objects.c` around lines 746–750.';
    expect(ChatComponent.stripThoughts(input)).toBe(expected);
  });

  it('should remove interleaved thinking blocks between response paragraphs', () => {
    const input = 'First paragraph.\n\n<div class="ai-thought">\nThinking...\n</div>\n\nSecond paragraph.';
    expect(ChatComponent.stripThoughts(input)).toBe('First paragraph.\n\nSecond paragraph.');
  });

  it('should remove unclosed thinking block during active streaming', () => {
    const input = 'Intro paragraph.\n\n<div class="ai-thought">\nActively thinking and not closed yet...';
    expect(ChatComponent.stripThoughts(input)).toBe('Intro paragraph.');
  });

  it('should return empty string when message contains only thinking blocks', () => {
    const inputClosed = '<div class="ai-thought">\nJust thinking...\n</div>';
    expect(ChatComponent.stripThoughts(inputClosed)).toBe('');

    const inputUnclosed = '<div class="ai-thought">\nActively thinking streaming start...';
    expect(ChatComponent.stripThoughts(inputUnclosed)).toBe('');
  });

  it('should preserve multi-line formatting and blank lines inside code blocks', () => {
    const input = `<div class="ai-thought">
Thinking about code...
</div>

Here is the code:

\`\`\`python
def calculate(a, b):


    # Notice the multiple blank lines above
    return a + b
\`\`\`

All done!`;

    const expected = `Here is the code:

\`\`\`python
def calculate(a, b):


    # Notice the multiple blank lines above
    return a + b
\`\`\`

All done!`;

    expect(ChatComponent.stripThoughts(input)).toBe(expected);
  });

  it('should handle case insensitivity and tag variations', () => {
    const input = '<DIV CLASS="ai-thought">\nThinking in uppercase tag...\n</DIV>\n\nResult text.';
    expect(ChatComponent.stripThoughts(input)).toBe('Result text.');

    const inputQuotes = "<div class='ai-thought'>\nThinking with single quotes...\n</div>\n\nResult text.";
    expect(ChatComponent.stripThoughts(inputQuotes)).toBe('Result text.');
  });

  it('should handle Windows CRLF line endings properly', () => {
    const input = '<div class="ai-thought">\r\nThinking...\r\n</div>\r\n\r\n\r\n\r\nResult text with CRLF.';
    expect(ChatComponent.stripThoughts(input)).toBe('Result text with CRLF.');
  });
});
