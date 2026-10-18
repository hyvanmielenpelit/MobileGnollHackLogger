import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, shareReplay, throwError } from 'rxjs';

/** The controller route prefix of the Terms endpoints. */
export const TERMS_ENDPOINT = '/api/legal/terms';

/** The only Terms document the SPA shows. */
export const OVERSEER_TERMS_KEY = 'overseer-terms';

/** A Terms document as `GET /api/legal/terms/{key}` returns it. */
export interface TermsDocument {
  key: string;
  title: string;
  /** ISO date, optionally with a revision suffix (`2026-10-18.2`); null when the server knows none. */
  version: string | null;
  /** The version as the document writes it, for display only. */
  versionLabel: string | null;
  /** Null when the server cannot read the file; the client then offers the wiki link only. */
  markdown: string | null;
  contentSha256: string | null;
  wikiUrl: string;
  privacyPolicyUrl: string | null;
}

/** The body of `POST /api/legal/terms/{key}/accept`: the version and hash the user read. */
export interface TermsAcceptRequest {
  version: string | null;
  contentSha256: string | null;
  /** The chat whose greeting waits on the acceptance, if any. */
  sessionId?: number;
}

export interface TermsAcceptResult {
  accepted: boolean;
  /** True when the server started the greeting of the chat named by `sessionId`. */
  greetingStarted: boolean;
}

/** True for the 409 the accept endpoint returns when the Terms changed after they were loaded. */
export function isTermsVersionChanged(error: unknown): boolean {
  return error instanceof HttpErrorResponse
    && error.status === 409
    && (error.error as { error?: unknown } | null)?.error === 'terms_version_changed';
}

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December'
];

/**
 * Formats a Terms version for display: `2026-10-18` as *18 October 2026*, `2026-10-18.2` as
 * *18 October 2026 (revision 2)*. Revision 1 reads as none. Input that is not such a version is
 * returned unchanged.
 */
export function formatTermsVersion(version: string | null | undefined): string | null {
  if (version === null || version === undefined) {
    return null;
  }
  const match = /^(\d{4})-(\d{2})-(\d{2})(?:\.(\d+))?$/.exec(version.trim());
  if (!match) {
    return version;
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (month < 1 || month > 12 || date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    return version;
  }
  const text = `${day} ${MONTH_NAMES[month - 1]} ${year}`;
  const revision = match[4] === undefined ? 1 : Number(match[4]);
  return revision > 1 ? `${text} (revision ${revision})` : text;
}

/**
 * The document text without its own `**Last updated:**` line, which the viewers show above the
 * text instead. Only a match within the first lines is removed.
 */
export function withoutLastUpdatedLine(markdown: string | null | undefined): string {
  if (!markdown) {
    return '';
  }
  const lines = markdown.split(/\r?\n/);
  const index = lines.slice(0, 20).findIndex(line => /^\s*\*{0,2}Last updated:/i.test(line));
  if (index < 0) {
    return markdown;
  }
  lines.splice(index, 1);
  return lines.join('\n').replace(/^\s*\n/, '');
}

/** Reads the Terms documents and records their acceptance. */
@Injectable({
  providedIn: 'root'
})
export class TermsService {
  private readonly http = inject(HttpClient);
  private readonly documents = new Map<string, Observable<TermsDocument>>();

  /** The document, fetched once per key; `reload` fetches it again and replaces the memo. A failed fetch is not kept. */
  getTerms(key: string = OVERSEER_TERMS_KEY, reload = false): Observable<TermsDocument> {
    const memo = reload ? undefined : this.documents.get(key);
    if (memo) {
      return memo;
    }
    const request$: Observable<TermsDocument> = this.http.get<TermsDocument>(`${TERMS_ENDPOINT}/${encodeURIComponent(key)}`).pipe(
      catchError(error => {
        if (this.documents.get(key) === request$) {
          this.documents.delete(key);
        }
        return throwError(() => error);
      }),
      shareReplay(1)
    );
    this.documents.set(key, request$);
    return request$;
  }

  /** Records that the signed-in user accepted the given version of the document. */
  accept(key: string, body: TermsAcceptRequest): Observable<TermsAcceptResult> {
    return this.http.post<TermsAcceptResult>(`${TERMS_ENDPOINT}/${encodeURIComponent(key)}/accept`, body);
  }
}
