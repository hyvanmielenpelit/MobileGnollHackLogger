import { Injectable } from '@angular/core';

export interface DebugLogEntry {
  timestamp: Date;
  message: string;
}

@Injectable({
  providedIn: 'root'
})
export class DebugService {
  private logs: DebugLogEntry[] = [];
  private readonly MAX_LOGS = 2000;

  constructor() { }

  log(message: string) {
    this.logs.push({
      timestamp: new Date(),
      message: message
    });
    if (this.logs.length > this.MAX_LOGS) {
      this.logs.shift(); // Remove oldest
    }
  }

  getLogs(): DebugLogEntry[] {
    return this.logs;
  }
}
