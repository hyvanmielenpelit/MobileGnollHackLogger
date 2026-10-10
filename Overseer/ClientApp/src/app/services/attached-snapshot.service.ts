import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
// Type-only: the chat is in the initial bundle, and the admin benchmark service is not.
import type { CaptureBenchmarkSnapshotResponse } from './admin-benchmark.service';

export interface SaveAttachedSnapshotRequest {
  sessionId: string;
  name: string;
  notes?: string | null;
  sourceGnollHackVersion?: string | null;
}

/* What saving a chat's attached snapshot would store, and the snapshots already saved from it. */
export interface AttachedSnapshotInfo {
  sessionId: number;
  hasSnapshot: boolean;
  charCount: number;
  sha256?: string | null;
  capturedAtUtc?: string | null;
  detectedGnollHackVersion?: string | null;
  existingBoards: AttachedSnapshotExistingBoard[];
}

export interface AttachedSnapshotExistingBoard {
  id: number;
  name: string;
  suiteId?: number | null;
  suiteName?: string | null;
  capturedAtUtc?: string | null;
  /* Its SHA-256 equals the current snapshot's. */
  isIdentical: boolean;
}

/** A chat's attached game snapshot, read and saved as a GnollBench board. */
@Injectable({
  providedIn: 'root'
})
export class AttachedSnapshotService {
  private http = inject(HttpClient);

  getAttachedSnapshotInfo(sessionId: string): Observable<AttachedSnapshotInfo> {
    return this.http.get<AttachedSnapshotInfo>(`/api/admin/benchmark/snapshots/attached/${sessionId}`);
  }

  saveAttachedSnapshot(req: SaveAttachedSnapshotRequest): Observable<CaptureBenchmarkSnapshotResponse> {
    return this.http.post<CaptureBenchmarkSnapshotResponse>('/api/admin/benchmark/snapshots/from-session', req);
  }
}
