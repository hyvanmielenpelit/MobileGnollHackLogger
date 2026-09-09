import * as Sentry from '@sentry/angular';
import { HttpErrorResponse } from '@angular/common/http';

/**
 * Determines whether an error is an expected HTTP failure response or a transient browser-native fetch/network dropout.
 *
 * Operational failures dropped from Sentry:
 * - 5xx / 4xx HttpErrorResponse: Already captured on backend or handled in UI.
 * - Browser-native fetch dropouts (Failed to fetch, AbortError, Load failed, etc.): Normal mobile/network disconnects.
 */
export function isHttpOrNetworkError(err: any): boolean {
  if (!err) return false;

  // 1. Direct type and name checks
  if (err instanceof HttpErrorResponse || err?.name === 'HttpErrorResponse') {
    return true;
  }

  // 2. Browser-native fetch and network error signatures
  const msg = typeof err === 'string'
    ? err
    : `${err?.name || ''} ${err?.message || ''} ${err?.statusText || ''} ${err?.toString?.() || ''}`;
  const lower = msg.toLowerCase();

  if (
    lower.includes('failed to fetch') ||
    lower.includes('networkerror') ||
    lower.includes('load failed') ||
    lower.includes('fetch failed') ||
    lower.includes('aborterror') ||
    lower.includes('the user aborted a request') ||
    lower.includes('network request failed') ||
    err?.name === 'AbortError' ||
    err?.name === 'TimeoutError'
  ) {
    return true;
  }

  // 3. Recursive unwrap for Zone.js / Angular wrapped errors
  if (err?.rejection && err.rejection !== err && isHttpOrNetworkError(err.rejection)) {
    return true;
  }
  if (err?.ngOriginalError && err.ngOriginalError !== err && isHttpOrNetworkError(err.ngOriginalError)) {
    return true;
  }
  if (err?.originalError && err.originalError !== err && isHttpOrNetworkError(err.originalError)) {
    return true;
  }
  if (err?.error && err.error !== err && isHttpOrNetworkError(err.error)) {
    return true;
  }

  return false;
}

/* Whether the chat currently on screen is confidential. Module-level rather than injected,
   because beforeSend is a plain callback registered at bootstrap and has no access to the
   injector -- and because an error thrown during Angular's own teardown must still find this
   value.

   The browser SDK posts through the server's own tunnel at /api/sentry/log, so the server
   drops confidential events too. This is the near end: it stops the event being assembled and
   sent at all, which also keeps the breadcrumb trail out of the request. */
let confidentialSessionActive = false;

/**
 * Marks the client as viewing a confidential chat, so no telemetry is sent while it is open.
 * Called when a session loads and cleared when one closes.
 */
export function setSentryConfidentialSession(isConfidential: boolean): void {
  confidentialSessionActive = isConfidential;
}

/** Whether telemetry is currently suppressed for a confidential chat. */
export function isSentryConfidentialSessionActive(): boolean {
  return confidentialSessionActive;
}

/**
 * Sentry beforeSend filter callback for Overseer.
 * Drops all transient network dropouts and client-side HTTP error responses.
 */
export function sentryBeforeSend(event: Sentry.ErrorEvent, hint: Sentry.EventHint): Sentry.ErrorEvent | null {
  /* Checked first, and unconditionally: a crash while a confidential chat is open can carry
     that chat's content in a message, a stack frame or a breadcrumb, and there is no way to
     tell from here which of those it is. */
  if (confidentialSessionActive) {
    return null;
  }

  const error = hint?.originalException;
  if (isHttpOrNetworkError(error)) {
    return null;
  }

  if (event.exception?.values?.some(v => {
    const typeAndValue = `${v.type || ''}: ${v.value || ''}`.toLowerCase();
    return (
      typeAndValue.includes('httperrorresponse') ||
      typeAndValue.includes('failed to fetch') ||
      typeAndValue.includes('networkerror') ||
      typeAndValue.includes('load failed') ||
      typeAndValue.includes('fetch failed') ||
      typeAndValue.includes('aborterror')
    );
  })) {
    return null;
  }

  return event;
}
