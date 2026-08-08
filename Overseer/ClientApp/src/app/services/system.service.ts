import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class SystemService {
  private http = inject(HttpClient);

  getVersion(): Observable<string> {
    return this.http.get<{ version: string }>('/api/system/version', {
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    }).pipe(
      map(res => res.version)
    );
  }
}
