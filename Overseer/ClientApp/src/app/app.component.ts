import { Component, OnInit, inject, ChangeDetectionStrategy } from '@angular/core';
import { RouterModule, Router, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { filter, take } from 'rxjs';
import { ClientBridgeService } from './services/client-bridge.service';
import { AuthService } from './services/auth.service';

@Component({
    selector: 'app-root',
    imports: [RouterModule],
    templateUrl: './app.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  private router = inject(Router);
  private clientBridge = inject(ClientBridgeService);
  protected auth = inject(AuthService);

  ngOnInit() {
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd || event instanceof NavigationCancel || event instanceof NavigationError),
      take(1)
    ).subscribe(() => {
      const loader = document.getElementById('global-loader');
      if (loader) {
        loader.style.display = 'none';
        loader.remove(); // Remove it completely from the DOM
      }
    });

    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd)
    ).subscribe((event: NavigationEnd) => {
      this.auth.connectionProblemUrl.set(null);
      if (this.clientBridge.isEmbedded()) {
        const path = event.urlAfterRedirects;
        this.clientBridge.notifyUrlChanged(path);
      }
    });
  }

  retryNavigation(url: string) {
    this.auth.connectionProblemUrl.set(null);
    this.router.navigateByUrl(url);
  }
}
