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

/**
 * Sentry beforeSend filter callback for Overseer.
 * Drops all transient network dropouts and client-side HTTP error responses.
 */
export function sentryBeforeSend(event: Sentry.ErrorEvent, hint: Sentry.EventHint): Sentry.ErrorEvent | null {
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
