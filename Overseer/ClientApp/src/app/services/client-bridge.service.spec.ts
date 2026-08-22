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
    expect(service.isEmbedded()).toBeFalse();
  });

  it('should detect WebView2 platform', () => {
    resetGlobals();
    if (!(window as any).chrome) {
      (window as any).chrome = {};
    }
    (window as any).chrome.webview = {
      postMessage: jasmine.createSpy('postMessage')
    };

    expect(service.getPlatform()).toBe('webview2');
    expect(service.isEmbedded()).toBeTrue();
  });

  it('should detect Android WebView platform', () => {
    resetGlobals();
    (window as any).GnollHackBridge = {
      onWebMessage: jasmine.createSpy('onWebMessage')
    };

    expect(service.getPlatform()).toBe('android');
    expect(service.isEmbedded()).toBeTrue();
  });

  it('should detect iOS WKWebView platform', () => {
    resetGlobals();
    (window as any).webkit = {
      messageHandlers: {
        gnollhackBridge: {
          postMessage: jasmine.createSpy('postMessage')
        }
      }
    };

    expect(service.getPlatform()).toBe('ios');
    expect(service.isEmbedded()).toBeTrue();
  });

  it('should post raw object message to WebView2', () => {
    resetGlobals();
    if (!(window as any).chrome) {
      (window as any).chrome = {};
    }
    const postMessageSpy = jasmine.createSpy('postMessage');
    (window as any).chrome.webview = { postMessage: postMessageSpy };

    const payload = { type: 'test', data: 123 };
    service.postMessage(payload);

    expect(postMessageSpy).toHaveBeenCalledWith(payload);
  });

  it('should post stringified message to Android', () => {
    resetGlobals();
    const onWebMessageSpy = jasmine.createSpy('onWebMessage');
    (window as any).GnollHackBridge = { onWebMessage: onWebMessageSpy };

    const payload = { type: 'test', data: 123 };
    service.postMessage(payload);

    expect(onWebMessageSpy).toHaveBeenCalledWith(JSON.stringify(payload));
  });

  it('should post stringified message to iOS', () => {
    resetGlobals();
    const postMessageSpy = jasmine.createSpy('postMessage');
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
    const postMessageSpy = spyOn(service, 'postMessage');

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

  it('should format notifyUrlChanged correctly', () => {
    const postMessageSpy = spyOn(service, 'postMessage');

    service.notifyUrlChanged('/chat?sessionId=123');
    expect(postMessageSpy).toHaveBeenCalledWith({
      type: 'spa_url_changed',
      url: '/chat?sessionId=123'
    });
  });
});
