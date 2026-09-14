import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ReleaseNote, ChangelogResponse } from './release-note.model';

@Injectable({
  providedIn: 'root'
})
export class ChangelogService {
  private http = inject(HttpClient);

  getReleaseNotes(): Observable<ChangelogResponse> {
    return this.http.get<ChangelogResponse>('/api/changelog');
  }
}
