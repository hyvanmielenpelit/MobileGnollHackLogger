import { Component, OnInit, inject } from '@angular/core';
import { RouterModule, Router, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { filter, take } from 'rxjs';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  title = 'ClientApp';
  private router = inject(Router);

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
  }
}
