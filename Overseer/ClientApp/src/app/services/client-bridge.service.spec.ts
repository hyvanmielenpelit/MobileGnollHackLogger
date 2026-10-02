import { TestBed } from '@angular/core/testing';
import { ClientBridgeService } from './client-bridge.service';

describe('ClientBridgeService', () => {
  let service: ClientBridgeService;
  let originalChromeWebview: any;
  let originalGnollHackBridge: any;
  let originalWebkit: any;

  const resetGlobals = () => {
    if ((window as any).chrome) {
      delete (window as any).chrome.webview;
    }
    delete (window as any).GnollHackBridge;
    delete (window as any).webkit;
  };

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ClientBridgeService);

    originalChromeWebview = (window as any).chrome?.webview;
    originalGnollHackBridge = (window as any).GnollHackBridge;
    originalWebkit = (window as any).webkit;

    resetGlobals();
  });

  afterEach(() => {
    resetGlobals();
    if ((window as any).chrome && originalChromeWebview !== undefined) {
      (window as any).chrome.webview = originalChromeWebview;
    }
    if (originalGnollHackBridge !== undefined) {
      (window as any).GnollHackBridge = originalGnollHackBridge;
    }
    if (originalWebkit !== undefined) {
      (window as any).webkit = originalWebkit;
    }
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should detect browser platform (null) by default', () => {
    resetGlobals();

    expect(service.getPlatform()).toBeNull();
    expect(service.isEmbedded()).toBe(false);
  });

  it('should detect WebView2 platform', () => {
    resetGlobals();
    if (!(window as any).chrome) {
      (window as any).chrome = {};
    }
    (window as any).chrome.webview = {
      postMessage: vi.fn().mockName('postMessage')
    };

    expect(service.getPlatform()).toBe('webview2');
    expect(service.isEmbedded()).toBe(true);
  });

  it('should detect Android WebView platform', () => {
    resetGlobals();
    (window as any).GnollHackBridge = {
      onWebMessage: vi.fn().mockName('onWebMessage')
    };

    expect(service.getPlatform()).toBe('android');
    expect(service.isEmbedded()).toBe(true);
  });

  it('should detect iOS WKWebView platform', () => {
    resetGlobals();
    (window as any).webkit = {
      messageHandlers: {
        gnollhackBridge: {
          postMessage: vi.fn().mockName('postMessage')
        }
      }
    };

    expect(service.getPlatform()).toBe('ios');
    expect(service.isEmbedded()).toBe(true);
  });

  it('should post raw object message to WebView2', () => {
    resetGlobals();
    if (!(window as any).chrome) {
      (window as any).chrome = {};
    }
    const postMessageSpy = vi.fn().mockName('postMessage');
    (window as any).chrome.webview = { postMessage: postMessageSpy };

    const payload = { type: 'test', data: 123 };
    service.postMessage(payload);

    expect(postMessageSpy).toHaveBeenCalledWith(payload);
  });

  it('should post stringified message to Android', () => {
    resetGlobals();
    const onWebMessageSpy = vi.fn().mockName('onWebMessage');
    (window as any).GnollHackBridge = { onWebMessage: onWebMessageSpy };

    const payload = { type: 'test', data: 123 };
    service.postMessage(payload);

    expect(onWebMessageSpy).toHaveBeenCalledWith(JSON.stringify(payload));
  });

  it('should post stringified message to iOS', () => {
    resetGlobals();
    const postMessageSpy = vi.fn().mockName('postMessage');
    (window as any).webkit = {
      messageHandlers: {
        gnollhackBridge: {
          postMessage: postMessageSpy
        }
      }
    };

    const payload = { type: 'test', data: 123 };
    service.postMessage(payload);

    expect(postMessageSpy).toHaveBeenCalledWith(JSON.stringify(payload));
  });

  it('should format notifySessionChanged correctly for numeric and string IDs and null', () => {
    const postMessageSpy = vi.spyOn(service, 'postMessage').mockReturnValue(undefined);

    service.notifySessionChanged(123);
    expect(postMessageSpy).toHaveBeenCalledWith({
      type: 'session_changed',
      sessionId: '123'
    });

    service.notifySessionChanged('456');
    expect(postMessageSpy).toHaveBeenCalledWith({
      type: 'session_changed',
      sessionId: '456'
    });

    service.notifySessionChanged(null);
    expect(postMessageSpy).toHaveBeenCalledWith({
      type: 'session_changed',
      sessionId: ''
    });

    service.notifySessionChanged(undefined);
    expect(postMessageSpy).toHaveBeenCalledWith({
      type: 'session_changed',
      sessionId: ''
    });
  });

  /* The default is "game on" so a GnollHack build predating the isGameOn field keeps the
     snapshot controls it has today. */
  it('should report isGameOn true until the host says otherwise', () => {
    expect(service.isGameOn()).toBe(true);
  });

  it('should report isGameOn false only after the host reports no running game', () => {
    service.setHostGameOn(false);
    expect(service.isGameOn()).toBe(false);

    service.setHostGameOn(true);
    expect(service.isGameOn()).toBe(true);

    service.setHostGameOn(null);
    expect(service.isGameOn()).toBe(true);
  });

  it('should remember no GnollHack version until one is reported', () => {
    expect(service.getHostGnollHackVersion()).toBeNull();
  });

  it('should store a reported GnollHack version trimmed', () => {
    service.setHostGnollHackVersion('  0.9.4 ');
    expect(service.getHostGnollHackVersion()).toBe('0.9.4');
  });

  /* A chat opened outside the game reports no version; the handoff's must survive it. */
  it('should keep the remembered GnollHack version when a blank one arrives', () => {
    service.setHostGnollHackVersion('0.9.4');

    service.setHostGnollHackVersion(null);
    service.setHostGnollHackVersion(undefined);
    service.setHostGnollHackVersion('');
    service.setHostGnollHackVersion('   ');

    expect(service.getHostGnollHackVersion()).toBe('0.9.4');
  });

  it('should format notifyUrlChanged correctly', () => {
    const postMessageSpy = vi.spyOn(service, 'postMessage').mockReturnValue(undefined);

    service.notifyUrlChanged('/chat?sessionId=123');
    expect(postMessageSpy).toHaveBeenCalledWith({
      type: 'spa_url_changed',
      url: '/chat?sessionId=123'
    });
  });
});
