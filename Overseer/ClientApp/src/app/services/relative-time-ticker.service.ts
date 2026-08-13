import { Injectable, NgZone } from '@angular/core';
import { Observable, interval, Subscription } from 'rxjs';
import { shareReplay } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class RelativeTimeTickerService {
  readonly tick$: Observable<number>;

  constructor(private ngZone: NgZone) {
    // Run the interval outside the Angular zone so it doesn't trigger global change detection on every tick.
    this.tick$ = new Observable<number>(observer => {
      let sub: Subscription;
      this.ngZone.runOutsideAngular(() => {
        // Tick every 15 seconds to update relative times
        sub = interval(15000).subscribe(val => observer.next(val));
      });
      return () => {
        if (sub) {
          sub.unsubscribe();
        }
      };
    }).pipe(
      // Share a single interval among all subscribers (e.g. all relative time pipes)
      shareReplay({ bufferSize: 1, refCount: true })
    );
  }
}
