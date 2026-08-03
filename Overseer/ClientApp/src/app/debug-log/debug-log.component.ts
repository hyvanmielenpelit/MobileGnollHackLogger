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
