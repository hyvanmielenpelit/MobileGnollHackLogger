/** Saves text as a file through a temporary object URL, revoked once the download has started. */
export function downloadTextFile(fileName: string, text: string, mimeType = 'application/yaml;charset=utf-8'): void {
  const blob = new Blob([text], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  try {
    anchor.click();
  } finally {
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}

/** Reduces a file name to lower-case `[a-z0-9._-]`, with whitespace as `-`. Empty input becomes `export`. */
export function safeFileName(input: string): string {
  const name = (input ?? '')
    .toLowerCase()
    .replace(/\s+/g, '-')
    .replace(/[^a-z0-9._-]/g, '')
    .replace(/-{2,}/g, '-')
    .replace(/_{2,}/g, '_')
    .replace(/\.{2,}/g, '.')
    .replace(/^[-.]+|[-.]+$/g, '');
  return name === '' ? 'export' : name;
}
