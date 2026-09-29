import { HttpErrorResponse } from '@angular/common/http';

/**
 * The client side of the key-save contract shared by the administrator's default keys and a
 * user's own keys: a key is checked with its provider before it is saved, and the outcome is kept
 * beside the key.
 */

/** The stored outcome of the last check. `null` means the key has never been checked. */
export type ApiKeyVerificationStatus = 'Verified' | 'NotVerified';

export interface ApiKeyVerification {
  status: ApiKeyVerificationStatus | null;
  checkedAtUtc: string | null;
  /** The warning of a verified key, or the failure of an unverified one. */
  message: string | null;
}

/** What one provider check called and what came back. `text` is all of it as plain text. */
export interface ApiKeyCheckDetail {
  request: string;
  httpStatus: number | null;
  httpReason: string | null;
  providerError: string | null;
  exception: string | null;
  elapsedMs: number;
  text: string;
}

/** `invalid`: the provider rejected the key. `unverifiable`: the check itself did not get an answer. */
export type ApiKeyRefusalVerdict = 'invalid' | 'unverifiable';

/** The body of a 400 (`invalid`) or 409 (`unverifiable`) answer to a key save. */
export interface ApiKeyRefusal {
  verdict: ApiKeyRefusalVerdict;
  message: string;
  detail: ApiKeyCheckDetail | null;
}

/** The refusal a 400 or 409 carries, or null for any other error, including a 400 `{ message }`. */
export function readApiKeyRefusal(error: HttpErrorResponse): ApiKeyRefusal | null {
  if (!error || (error.status !== 400 && error.status !== 409)) {
    return null;
  }

  let body: unknown = error.error;
  if (typeof body === 'string') {
    try {
      body = JSON.parse(body);
    } catch {
      return null;
    }
  }
  if (!body || typeof body !== 'object') {
    return null;
  }

  const record = body as Record<string, unknown>;
  const verdict = record['verdict'];
  if (verdict !== 'invalid' && verdict !== 'unverifiable') {
    return null;
  }

  return {
    verdict,
    message: typeof record['message'] === 'string' ? record['message'] : '',
    detail: readDetail(record['detail'])
  };
}

function readDetail(value: unknown): ApiKeyCheckDetail | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const d = value as Record<string, unknown>;
  const text = (key: string): string | null =>
    typeof d[key] === 'string' && (d[key] as string).trim() ? (d[key] as string) : null;
  return {
    request: text('request') ?? '',
    httpStatus: typeof d['httpStatus'] === 'number' ? d['httpStatus'] as number : null,
    httpReason: text('httpReason'),
    providerError: text('providerError'),
    exception: text('exception'),
    elapsedMs: typeof d['elapsedMs'] === 'number' ? d['elapsedMs'] as number : 0,
    text: text('text') ?? ''
  };
}

/** The `message` of a plain 400 / 404 body, a plain-string body, or null. */
export function readServerMessage(error: HttpErrorResponse | null | undefined): string | null {
  const body = error?.error;
  if (typeof body === 'string' && body.trim()) {
    return body.trim();
  }
  if (body && typeof body === 'object' && typeof body.message === 'string' && body.message.trim()) {
    return body.message.trim();
  }
  return null;
}

/** The label word for a stored verification, or null when the key has never been checked. */
export function verificationLabel(verification: ApiKeyVerification | null | undefined): string | null {
  switch (verification?.status) {
    case 'Verified':
      return 'Verified';
    case 'NotVerified':
      return 'Not verified';
    default:
      return null;
  }
}

/** Local date and time of a stored UTC timestamp, or '' when there is none. */
export function formatCheckedAt(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }
  const parsed = new Date(iso);
  return isNaN(parsed.getTime()) ? '' : parsed.toLocaleString();
}

/** The label's tooltip: when the key was checked, then the stored message. */
export function verificationTooltip(verification: ApiKeyVerification | null | undefined): string {
  if (!verification) {
    return '';
  }
  const lines: string[] = [];
  const checked = formatCheckedAt(verification.checkedAtUtc);
  if (checked) {
    lines.push(`Checked ${checked}`);
  }
  if (verification.message?.trim()) {
    lines.push(verification.message.trim());
  }
  return lines.join('\n');
}

/** The response part of a check: the HTTP status and reason, or *No response*. */
export function describeCheckResponse(detail: ApiKeyCheckDetail): string {
  if (detail.httpStatus === null) {
    return 'No response';
  }
  return detail.httpReason ? `HTTP ${detail.httpStatus} ${detail.httpReason}` : `HTTP ${detail.httpStatus}`;
}

/** An elapsed time in milliseconds, as *850 ms* or *12.3 s*. */
export function formatElapsed(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) {
    return '';
  }
  if (ms < 1000) {
    return `${Math.round(ms)} ms`;
  }
  return `${(ms / 1000).toFixed(1)} s`;
}
