import { Component, OnInit } from '@angular/core';
import { DebugService, DebugLogEntry } from '../services/debug.service';
import { CommonModule, Location } from '@angular/common';

@Component({
  selector: 'app-debug-log',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './debug-log.component.html',
  styleUrl: './debug-log.component.scss'
})
export class DebugLogComponent implements OnInit {
  logs: DebugLogEntry[] = [];
  copiedLog: DebugLogEntry | null = null;
  isCopiedAll: boolean = false;

  constructor(private debugService: DebugService, private location: Location) {}

  ngOnInit(): void {
    this.logs = this.debugService.getLogs();
  }

  goBack(): void {
    this.location.back();
  }

  clearLogs(): void {
    this.logs.length = 0; // Clear the array in-place
  }

  isLongLog(message: string): boolean {
    return message.length > 500;
  }

  getShortMessage(message: string): string {
    return message.substring(0, 500) + '... [Truncated]';
  }

  copyAllLogs(): void {
    const fullLogText = this.logs.map(log => {
      const date = new Date(log.timestamp);
      const pad = (n: number, w: number = 2) => n.toString().padStart(w, '0');
      const h = pad(date.getHours());
      const m = pad(date.getMinutes());
      const s = pad(date.getSeconds());
      const ms = pad(date.getMilliseconds(), 3);
      return `[${h}:${m}:${s}.${ms}] ${log.message}`;
    }).join('\n');

    navigator.clipboard.writeText(fullLogText).then(() => {
      this.isCopiedAll = true;
      setTimeout(() => {
        this.isCopiedAll = false;
      }, 2000);
    }).catch(err => {
      console.error('Failed to copy all logs: ', err);
    });
  }

  copyToClipboard(log: DebugLogEntry): void {
    navigator.clipboard.writeText(log.message).then(() => {
      this.copiedLog = log;
      setTimeout(() => {
        if (this.copiedLog === log) {
          this.copiedLog = null;
        }
      }, 2000);
    }).catch(err => {
      console.error('Failed to copy text: ', err);
    });
  }
}
