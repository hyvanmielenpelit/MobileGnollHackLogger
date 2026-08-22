import { Component, OnInit, ChangeDetectionStrategy, inject } from '@angular/core';
import { DebugService, DebugLogEntry } from '../services/debug.service';
import { CommonModule, Location } from '@angular/common';
import { ClientBridgeService } from '../services/client-bridge.service';

@Component({
    selector: 'app-debug-log',
    imports: [CommonModule],
    templateUrl: './debug-log.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './debug-log.component.scss'
})
export class DebugLogComponent implements OnInit {
  logs: DebugLogEntry[] = [];
  copiedLog: DebugLogEntry | null = null;
  isCopiedAll: boolean = false;
  isShared: boolean = false;
  isDownloaded: boolean = false;
  isInWebView: boolean = false;
  canShare: boolean = false;

  private clientBridge = inject(ClientBridgeService);

  constructor(private debugService: DebugService, private location: Location) {}

  ngOnInit(): void {
    this.logs = this.debugService.getLogs();
    this.isInWebView = this.clientBridge.isEmbedded();
    
    if (this.isInWebView) {
      this.canShare = true;
    } else {
      try {
        const testFile = new File([''], 'test.txt', { type: 'text/plain' });
        this.canShare = navigator.canShare && navigator.canShare({ files: [testFile] });
      } catch (e) {
        this.canShare = false;
      }
    }
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

  getLogText(): string {
    return this.logs.map(log => {
      const date = new Date(log.timestamp);
      const pad = (n: number, w: number = 2) => n.toString().padStart(w, '0');
      const h = pad(date.getHours());
      const m = pad(date.getMinutes());
      const s = pad(date.getSeconds());
      const ms = pad(date.getMilliseconds(), 3);
      return `[${h}:${m}:${s}.${ms}] ${log.message}`;
    }).join('\n');
  }

  shareLogs(): void {
    const content = this.getLogText();
    const filename = 'overseer-debug-log.txt';

    if (this.clientBridge.isEmbedded()) {
      const request = { type: 'share_text_file', filename, content };
      this.clientBridge.postMessage(request);
      this.isShared = true;
      setTimeout(() => this.isShared = false, 2000);
    } else {
      const file = new File([content], filename, { type: 'text/plain' });
      if (navigator.canShare && navigator.canShare({ files: [file] })) {
        navigator.share({ files: [file] })
          .then(() => {
            this.isShared = true;
            setTimeout(() => this.isShared = false, 2000);
          })
          .catch(err => {
            if (err.name !== 'AbortError') {
              console.error('Failed to share log file: ', err);
            }
          });
      }
    }
  }

  downloadLogs(): void {
    const content = this.getLogText();
    const date = new Date();
    const pad = (n: number) => n.toString().padStart(2, '0');
    const timestamp = `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`;
    const filename = `overseer-debug-log-${timestamp}.txt`;

    const blob = new Blob([content], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    
    this.isDownloaded = true;
    setTimeout(() => this.isDownloaded = false, 2000);
  }

  copyAllLogs(): void {
    const fullLogText = this.getLogText();

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
