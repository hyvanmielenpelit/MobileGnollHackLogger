import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';

/**
 * Tells the Benchmark tab's OnPush components that state a service holds has changed outside their own
 * events: a poll, an HTTP response, a timer. Each component subscribes and checks its view.
 */
@Injectable()
export class BenchmarkViewSync {
  private readonly changes = new Subject<void>();

  readonly changed$: Observable<void> = this.changes.asObservable();

  notify(): void {
    this.changes.next();
  }
}
