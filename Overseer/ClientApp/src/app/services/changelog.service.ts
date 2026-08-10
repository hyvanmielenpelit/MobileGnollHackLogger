import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ReleaseNote } from './release-note.model';

@Injectable({
  providedIn: 'root'
})
export class ChangelogService {
  private http = inject(HttpClient);
  private storageKey = 'overseer_last_seen_changelog';

  getReleaseNotes(): Observable<ReleaseNote[]> {
    return this.http.get<ReleaseNote[]>('/api/changelog');
  }

  hasNewMajorOrMinorVersion(latestVersion: string): boolean {
    const lastSeen = localStorage.getItem(this.storageKey) || '0.0.0';
    return this.compareMajorMinor(latestVersion, lastSeen) > 0;
  }

  markAsSeen(version: string): void {
    localStorage.setItem(this.storageKey, version);
  }

  // Returns > 0 if v1 > v2 (only checking major.minor)
  private compareMajorMinor(v1: string, v2: string): number {
    const p1 = v1.split('.').map(n => parseInt(n, 10));
    const p2 = v2.split('.').map(n => parseInt(n, 10));
    
    const major1 = isNaN(p1[0]) ? 0 : p1[0];
    const minor1 = isNaN(p1[1]) ? 0 : p1[1];
    
    const major2 = isNaN(p2[0]) ? 0 : p2[0];
    const minor2 = isNaN(p2[1]) ? 0 : p2[1];

    if (major1 !== major2) {
      return major1 - major2;
    }
    return minor1 - minor2;
  }
}
