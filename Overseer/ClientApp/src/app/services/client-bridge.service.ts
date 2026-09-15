import { Injectable } from '@angular/core';

export type ClientBridgePlatform = 'webview2' | 'android' | 'ios' | null;

@Injectable({
  providedIn: 'root'
})
export class ClientBridgeService {

  /* Whether the embedding GnollHack host has a game running. Set once from the handoff
     redirect's gameOn query parameter; null until then, and for hosts that never report it. */
  private hostGameOn: boolean | null = null;

  setHostGameOn(value: boolean | null): void {
    this.hostGameOn = value;
  }

  /* True unless the host explicitly reported no running game, so a host that reports
     nothing keeps the snapshot controls. */
  isGameOn(): boolean {
    return this.hostGameOn !== false;
  }

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
