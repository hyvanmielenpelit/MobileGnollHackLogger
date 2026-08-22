import { HttpErrorResponse } from '@angular/common/http';
import * as Sentry from '@sentry/angular';
import { isHttpOrNetworkError, sentryBeforeSend } from './sentry-filter.util';

describe('sentry-filter.util', () => {
  describe('isHttpOrNetworkError', () => {
    describe('Normal operational & runtime errors (MUST NOT be filtered)', () => {
      it('should return false for null and undefined', () => {
        expect(isHttpOrNetworkError(null)).toBeFalse();
        expect(isHttpOrNetworkError(undefined)).toBeFalse();
      });

      it('should return false for genuine TypeError (e.g. null reference / property access)', () => {
        const error = new TypeError("Cannot read properties of undefined (reading 'username')");
        expect(isHttpOrNetworkError(error)).toBeFalse();
      });

      it('should return false for ReferenceError', () => {
        const error = new ReferenceError('foo is not defined');
        expect(isHttpOrNetworkError(error)).toBeFalse();
      });

      it('should return false for standard application Error', () => {
        const error = new Error('Invalid business logic state in component');
        expect(isHttpOrNetworkError(error)).toBeFalse();
      });

      it('should return false for RangeError', () => {
        const error = new RangeError('Maximum call stack size exceeded');
        expect(isHttpOrNetworkError(error)).toBeFalse();
      });

      it('should return false for empty or non-error objects', () => {
        expect(isHttpOrNetworkError({})).toBeFalse();
        expect(isHttpOrNetworkError({ message: 'Success event' })).toBeFalse();
      });
    });

    describe('Network dropouts & HTTP errors (MUST be filtered)', () => {
      it('should return true for Chrome / Android WebView TypeError: Failed to fetch', () => {
        const error = new TypeError('Failed to fetch');
        expect(isHttpOrNetworkError(error)).toBeTrue();
      });

      it('should return true for Safari / WebKit TypeError: Load failed', () => {
        const error = new TypeError('Load failed');
        expect(isHttpOrNetworkError(error)).toBeTrue();
      });

      it('should return true for Firefox TypeError: NetworkError when attempting to fetch resource', () => {
        const error = new TypeError('NetworkError when attempting to fetch resource.');
        expect(isHttpOrNetworkError(error)).toBeTrue();
      });

      it('should return true for DOMException AbortError', () => {
        const error = new DOMException('The user aborted a request.', 'AbortError');
        expect(isHttpOrNetworkError(error)).toBeTrue();
      });

      it('should return true for object with name AbortError or TimeoutError', () => {
        expect(isHttpOrNetworkError({ name: 'AbortError', message: 'Aborted' })).toBeTrue();
        expect(isHttpOrNetworkError({ name: 'TimeoutError', message: 'Timed out' })).toBeTrue();
      });

      it('should return true for Angular HttpErrorResponse (status 0, 404, 500, 503)', () => {
        expect(isHttpOrNetworkError(new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' }))).toBeTrue();
        expect(isHttpOrNetworkError(new HttpErrorResponse({ status: 404, statusText: 'Not Found' }))).toBeTrue();
        expect(isHttpOrNetworkError(new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }))).toBeTrue();
        expect(isHttpOrNetworkError(new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }))).toBeTrue();
      });

      it('should return true for duck-typed HttpErrorResponse name', () => {
        expect(isHttpOrNetworkError({ name: 'HttpErrorResponse', status: 500 })).toBeTrue();
      });

      it('should return true for Zone.js wrapped promise rejection', () => {
        const wrapped = {
          rejection: new TypeError('Failed to fetch')
        };
        expect(isHttpOrNetworkError(wrapped)).toBeTrue();
      });

      it('should return true for Angular ngOriginalError wrapper', () => {
        const wrapped = {
          ngOriginalError: new TypeError('Failed to fetch')
        };
        expect(isHttpOrNetworkError(wrapped)).toBeTrue();
      });

      it('should return true for originalError wrapper', () => {
        const wrapped = {
          originalError: new HttpErrorResponse({ status: 500 })
        };
        expect(isHttpOrNetworkError(wrapped)).toBeTrue();
      });

      it('should return true for nested RxJS error wrapper', () => {
        const wrapped = {
          error: new TypeError('Failed to fetch')
        };
        expect(isHttpOrNetworkError(wrapped)).toBeTrue();
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
