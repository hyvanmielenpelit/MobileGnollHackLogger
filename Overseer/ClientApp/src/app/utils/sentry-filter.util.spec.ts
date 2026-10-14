import { HttpErrorResponse } from '@angular/common/http';
import * as Sentry from '@sentry/angular';
import { isHttpOrNetworkError, sentryBeforeSend, setSentryConfidentialSession } from './sentry-filter.util';

describe('sentry-filter.util', () => {
  /* The confidential-session flag is module-level, so it outlives a spec and a spec file. Any
     suite that opens a confidential or incognito chat leaves it set, and depending on the
     order in which the runner takes the files that suite can run first -- at which point every assertion here that an
     event is KEPT fails, because sentryBeforeSend drops everything while the flag is on.
     Establishing the state it depends on is this suite's job. */
  beforeEach(() => {
    setSentryConfidentialSession(false);
  });
  describe('isHttpOrNetworkError', () => {
    describe('Normal operational & runtime errors (MUST NOT be filtered)', () => {
      it.each<{ name: string; error: () => unknown }>([
        { name: 'null', error: () => null },
        { name: 'undefined', error: () => undefined },
        // e.g. null reference / property access
        { name: 'genuine TypeError', error: () => new TypeError("Cannot read properties of undefined (reading 'username')") },
        { name: 'ReferenceError', error: () => new ReferenceError('foo is not defined') },
        { name: 'standard application Error', error: () => new Error('Invalid business logic state in component') },
        { name: 'RangeError', error: () => new RangeError('Maximum call stack size exceeded') },
        { name: 'an empty object', error: () => ({}) },
        { name: 'a non-error object', error: () => ({ message: 'Success event' }) }
      ])('should return false for $name', ({ error }) => {
        expect(isHttpOrNetworkError(error())).toBe(false);
      });
    });

    describe('Network dropouts & HTTP errors (MUST be filtered)', () => {
      it.each<{ name: string; error: () => unknown }>([
        { name: 'Chrome / Android WebView TypeError: Failed to fetch', error: () => new TypeError('Failed to fetch') },
        { name: 'Safari / WebKit TypeError: Load failed', error: () => new TypeError('Load failed') },
        { name: 'Firefox TypeError: NetworkError when attempting to fetch resource', error: () => new TypeError('NetworkError when attempting to fetch resource.') },
        { name: 'DOMException AbortError', error: () => new DOMException('The user aborted a request.', 'AbortError') },
        { name: 'object with name AbortError', error: () => ({ name: 'AbortError', message: 'Aborted' }) },
        { name: 'object with name TimeoutError', error: () => ({ name: 'TimeoutError', message: 'Timed out' }) },
        { name: 'Angular HttpErrorResponse status 0', error: () => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' }) },
        { name: 'Angular HttpErrorResponse status 404', error: () => new HttpErrorResponse({ status: 404, statusText: 'Not Found' }) },
        { name: 'Angular HttpErrorResponse status 500', error: () => new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }) },
        { name: 'Angular HttpErrorResponse status 503', error: () => new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }) },
        { name: 'duck-typed HttpErrorResponse name', error: () => ({ name: 'HttpErrorResponse', status: 500 }) },
        { name: 'Zone.js wrapped promise rejection', error: () => ({ rejection: new TypeError('Failed to fetch') }) },
        { name: 'Angular ngOriginalError wrapper', error: () => ({ ngOriginalError: new TypeError('Failed to fetch') }) },
        { name: 'originalError wrapper', error: () => ({ originalError: new HttpErrorResponse({ status: 500 }) }) },
        { name: 'nested RxJS error wrapper', error: () => ({ error: new TypeError('Failed to fetch') }) }
      ])('should return true for $name', ({ error }) => {
        expect(isHttpOrNetworkError(error())).toBe(true);
      });
    });
  });

  describe('sentryBeforeSend', () => {
    it('should preserve genuine application errors with full event payload', () => {
      const mockEvent: Sentry.ErrorEvent = {
        type: undefined,
        event_id: '1234567890abcdef',
        level: 'error',
        exception: {
          values: [
            {
              type: 'TypeError',
              value: "Cannot read properties of null (reading 'item')"
            }
          ]
        }
      };
      const mockHint: Sentry.EventHint = {
        originalException: new TypeError("Cannot read properties of null (reading 'item')")
      };

      const result = sentryBeforeSend(mockEvent, mockHint);
      expect(result).toBe(mockEvent);
    });

    it('should drop event when hint.originalException is TypeError: Failed to fetch', () => {
      const mockEvent: Sentry.ErrorEvent = {
        type: undefined,
        event_id: 'fetch_error_event',
        level: 'error'
      };
      const mockHint: Sentry.EventHint = {
        originalException: new TypeError('Failed to fetch')
      };

      const result = sentryBeforeSend(mockEvent, mockHint);
      expect(result).toBeNull();
    });

    it('should drop event when hint.originalException is HttpErrorResponse', () => {
      const mockEvent: Sentry.ErrorEvent = {
        type: undefined,
        event_id: 'http_error_event',
        level: 'error'
      };
      const mockHint: Sentry.EventHint = {
        originalException: new HttpErrorResponse({ status: 500, statusText: 'Server Error' })
      };

      const result = sentryBeforeSend(mockEvent, mockHint);
      expect(result).toBeNull();
    });

    it('should drop event when event.exception.values contains Failed to fetch', () => {
      const mockEvent: Sentry.ErrorEvent = {
        type: undefined,
        event_id: 'exception_value_fetch_error',
        level: 'error',
        exception: {
          values: [
            {
              type: 'TypeError',
              value: 'Failed to fetch'
            }
          ]
        }
      };
      const mockHint: Sentry.EventHint = {};

      const result = sentryBeforeSend(mockEvent, mockHint);
      expect(result).toBeNull();
    });

    it('should drop event when event.exception.values contains HttpErrorResponse', () => {
      const mockEvent: Sentry.ErrorEvent = {
        type: undefined,
        event_id: 'exception_value_http_error',
        level: 'error',
        exception: {
          values: [
            {
              type: 'HttpErrorResponse',
              value: 'Http failure response for /api/models: 500 Internal Server Error'
            }
          ]
        }
      };
      const mockHint: Sentry.EventHint = {};

      const result = sentryBeforeSend(mockEvent, mockHint);
      expect(result).toBeNull();
    });
  });
});
