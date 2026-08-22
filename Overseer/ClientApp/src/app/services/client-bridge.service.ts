import { Injectable } from '@angular/core';

export type ClientBridgePlatform = 'webview2' | 'android' | 'ios' | null;

@Injectable({
  providedIn: 'root'
})
export class ClientBridgeService {

  getPlatform(): ClientBridgePlatform {
    if (typeof window === 'undefined') return null;
    if ((window as any).chrome?.webview?.postMessage) return 'webview2';
    if ((window as any).GnollHackBridge?.onWebMessage) return 'android';
    if ((window as any).webkit?.messageHandlers?.gnollhackBridge?.postMessage) return 'ios';
    return null;
  }

  isEmbedded(): boolean {
    return this.getPlatform() !== null;
  }

  postMessage(message: { type: string; [key: string]: any }): void {
    const platform = this.getPlatform();
    if (!platform) return;

    try {
      switch (platform) {
        case 'webview2':
          (window as any).chrome.webview.postMessage(message);
          break;
        case 'android':
          (window as any).GnollHackBridge.onWebMessage(JSON.stringify(message));
          break;
        case 'ios':
          (window as any).webkit.messageHandlers.gnollhackBridge.postMessage(JSON.stringify(message));
          break;
      }
    } catch (err) {
      console.warn('[ClientBridgeService] Failed to post message to native host:', err);
    }
  }

  notifySessionChanged(sessionId?: number | string | null): void {
    this.postMessage({
      type: 'session_changed',
      sessionId: sessionId !== null && sessionId !== undefined ? sessionId.toString() : ''
    });
  }

  notifyUrlChanged(url: string): void {
    this.postMessage({
      type: 'spa_url_changed',
      url
    });
  }
}
