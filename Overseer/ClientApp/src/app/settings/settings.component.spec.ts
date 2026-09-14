import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, ParamMap, convertToParamMap } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError, Subject } from 'rxjs';
import { SettingsComponent, SettingsSection } from './settings.component';
import {
  SettingsService,
  UserAiSettings,
  ConfidentialFloor,
  DlpFloor,
  ProvidedModelConfidentialStatus
} from '../services/settings.service';
import { ChatService } from '../services/chat.service';

describe('SettingsComponent', () => {
  let component: SettingsComponent;
  let fixture: ComponentFixture<SettingsComponent>;
  let settingsService: SettingsService;

  /** Activates a section directly, as the route param would, and re-renders. */
  function showSection(section: SettingsSection) {
    component.activeSection = section;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    settingsService = TestBed.inject(SettingsService);
  });

  describe('Initialization (ngOnInit)', () => {
    it('should populate settings when getSettings succeeds normally', () => {
      const mockSettings: UserAiSettings = {
        hasApiKey: true,
        hasModel: true,
        spoilerFreeMode: false,
        showSourceCodeReferences: true,
        showThoughtsAndTools: 1,
        enableWebSearch: true,
        enableToolUse: true,
        enableSubAgents: false,
        enableClientTools: false,
        enableGameActions: true,
        maxResultLength: 5000,
        maxCallsPerSession: 10,
        maxToolIterations: 3,
        maxParallelToolCalls: 4,
        showParallelBadge: false,
        parallelBadgeEnabled: true,
        showContextWindowUsage: false,
        showChatCost: false,
        requestTimeout: 60
      };
      spyOn(settingsService, 'getSettings').and.returnValue(of(mockSettings));

      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.spoilerFreeMode).toBeFalse();
      expect(component.showSourceCodeReferences).toBeTrue();
      expect(component.showParallelBadge).toBeFalse();
      expect(component.parallelBadgeEnabled).toBeTrue();
      expect(component.showContextWindowUsage).toBeFalse();
      expect(component.showChatCost).toBeFalse();
      expect(component.showThoughtsAndTools).toBe(1);
      expect(component.enableSubAgents).toBeFalse();
      expect(component.enableClientTools).toBeFalse();
      expect(component.enableGameActions).toBeTrue();
      expect(component.maxResultLength).toBe(5000);
      expect(component.maxCallsPerSession).toBe(10);
      expect(component.maxToolIterations).toBe(3);
      expect(component.maxParallelToolCalls).toBe(4);
      expect(component.requestTimeout).toBe(60);
    });

    it('should catch TypeError: Failed to fetch on getSettings without unhandled error', () => {
      spyOn(settingsService, 'getSettings').and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      expect(() => {
        fixture = TestBed.createComponent(SettingsComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
      }).not.toThrow();
    });
  });

  describe('auto-save', () => {
    beforeEach(() => {
      spyOn(settingsService, 'getSettings').and.returnValue(of({
        hasApiKey: true,
        spoilerFreeMode: true,
        showThoughtsAndTools: 0,
        performanceLimits: {
          maxResultLength: { min: 1000, max: 50000, defaultValue: 8000 },
          maxCallsPerSession: { min: 5, max: 250, defaultValue: 50 },
          maxToolIterations: { min: 3, max: 30, defaultValue: 10 },
          maxParallelToolCalls: { min: 1, max: 10, defaultValue: 4 },
          requestTimeout: { min: 10, max: 3600, defaultValue: 60 }
        }
      }));
      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
    });

    it('should default showChatCost to true and bind it to its checkbox', async () => {
      expect(component.showChatCost).toBeTrue();

      // ngModel writes the value to the DOM asynchronously, so let it settle first.
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const checkbox = compiled.querySelector('input[name="showChatCost"]') as HTMLInputElement;
      expect(checkbox).toBeTruthy();
      expect(checkbox.checked).toBeTrue();
    });

    it('should forward showChatCost as the sixteenth saveSettings argument', fakeAsync(() => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.showChatCost = false;
      component.onSettingChange();
      tick();

      expect(saveSpy).toHaveBeenCalled();
      expect(saveSpy.calls.mostRecent().args[15]).toBeFalse();
    }));

    it('should not trigger save on initial data load', fakeAsync(() => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));
      tick(1000);
      expect(saveSpy).not.toHaveBeenCalled();
    }));

    it('should trigger immediate save on boolean setting change', fakeAsync(() => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));
      const thoughtsSpy = spyOn(settingsService.showThoughtsAndToolsUpdated, 'next');

      component.spoilerFreeMode = false;
      component.onSettingChange();
      tick();

      expect(saveSpy).toHaveBeenCalled();
      expect(component.saveState).toBe('saved');
      expect(thoughtsSpy).toHaveBeenCalledWith(0);
    }));

    it('should debounce numeric input changes by 500ms', fakeAsync(() => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.maxResultLength = 12000;
      component.onNumberInputChange();

      tick(300);
      expect(saveSpy).not.toHaveBeenCalled();

      tick(200);
      expect(saveSpy).toHaveBeenCalledTimes(1);
      expect(component.saveState).toBe('saved');
    }));

    it('should batch rapid successive numeric changes into a single save', fakeAsync(() => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.maxResultLength = 10000;
      component.onNumberInputChange();
      tick(200);

      component.maxResultLength = 15000;
      component.onNumberInputChange();
      tick(200);

      component.maxResultLength = 20000;
      component.onNumberInputChange();
      tick(500);

      expect(saveSpy).toHaveBeenCalledTimes(1);
    }));

    it('should set validationErrors and prevent save on blur with out-of-range value', fakeAsync(() => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.requestTimeout = 999999;
      component.onNumberInputBlur('requestTimeout');
      tick();

      expect(component.validationErrors['requestTimeout']).toBeDefined();
      expect(saveSpy).not.toHaveBeenCalled();
    }));

    it('should catch save error, set saveState to error, and keep pipeline alive for subsequent saves', fakeAsync(() => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }))
      );

      component.spoilerFreeMode = false;
      component.onSettingChange();
      tick();

      expect(saveSpy).toHaveBeenCalledTimes(1);
      expect(component.saveState).toBe('error');

      // Subsequent valid save should work
      saveSpy.and.returnValue(of({ message: 'Saved' } as any));
      component.spoilerFreeMode = true;
      component.onSettingChange();
      tick();

      expect(saveSpy).toHaveBeenCalledTimes(2);
      expect(component.saveState).toBe('saved');
    }));

    it('canDeactivate should resolve immediately when no changes are pending', async () => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));
      const canLeave = await component.canDeactivate();

      expect(canLeave).toBeTrue();
      expect(saveSpy).not.toHaveBeenCalled();
    });

    it('canDeactivate should await in-flight save when changes are pending', async () => {
      const saveSubject = new Subject<any>();
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(saveSubject.asObservable());

      component.spoilerFreeMode = false;
      component.onSettingChange();

      let resolved = false;
      const canDeactivatePromise = component.canDeactivate().then((res) => {
        resolved = true;
        return res;
      });

      expect(saveSpy).toHaveBeenCalled();
      expect(resolved).toBeFalse();

      saveSubject.next({ message: 'Saved' });
      saveSubject.complete();

      const result = await canDeactivatePromise;
      expect(result).toBeTrue();
      expect(resolved).toBeTrue();
    });

    it('canDeactivate should revert invalid fields to last saved before saving', async () => {
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.lastSavedRequestTimeout = 60;
      component.requestTimeout = 999999;
      component.onNumberInputChange();

      const result = await component.canDeactivate();

      expect(result).toBeTrue();
      expect(component.requestTimeout).toBe(60);
      expect(saveSpy).toHaveBeenCalled();
    });
  });

  describe('Chat Data Management', () => {
    let chatService: any;

    beforeEach(() => {
      chatService = TestBed.inject(ChatService);
      spyOn(settingsService, 'getSettings').and.returnValue(of({
        hasApiKey: true
      } as any));
      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
    });

    it('should load chat metrics on loadChatMetrics', () => {
      spyOn(chatService, 'getSessions').and.returnValue(of({
        body: { activeCount: 45, pinnedCount: 3, maxQuota: 50, maxPinned: 5 }
      } as any));
      spyOn(chatService, 'getTrashSessions').and.returnValue(of([
        { id: 1, title: 'Trash 1' },
        { id: 2, title: 'Trash 2' }
      ] as any));

      component.loadChatMetrics();

      expect(component.activeSessionCount).toBe(45);
      expect(component.pinnedSessionCount).toBe(3);
      expect(component.maxSessionQuota).toBe(50);
      expect(component.maxPinnedQuota).toBe(5);
      expect(component.trashCount).toBe(2);
    });

    it('should compute bulkDeleteTargetCount correctly', () => {
      component.activeSessionCount = 45;
      component.pinnedSessionCount = 5;
      component.includePinnedInBulkDelete = false;

      expect(component.bulkDeleteTargetCount).toBe(40);

      component.includePinnedInBulkDelete = true;
      expect(component.bulkDeleteTargetCount).toBe(45);
    });

    it('should bulk delete active chats and reload metrics', () => {
      const closeSpy = jasmine.createSpy('close');
      component.settingsBulkDeleteDialog = { nativeElement: { close: closeSpy } } as any;
      spyOn(chatService, 'bulkDeleteSessions').and.returnValue(of({ count: 42 }));
      const metricsSpy = spyOn(component, 'loadChatMetrics');
      const toastSpy = spyOn(component, 'showToast');

      component.includePinnedInBulkDelete = true;
      component.confirmSettingsBulkDelete();

      expect(chatService.bulkDeleteSessions).toHaveBeenCalledWith(true);
      expect(closeSpy).toHaveBeenCalled();
      expect(metricsSpy).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith('Active chats moved to trash successfully!');
    });

    it('should unpin all chats and reload metrics', () => {
      const closeSpy = jasmine.createSpy('close');
      component.settingsUnpinAllDialog = { nativeElement: { close: closeSpy } } as any;
      spyOn(chatService, 'unpinAllSessions').and.returnValue(of({ count: 3 }));
      const metricsSpy = spyOn(component, 'loadChatMetrics');
      const toastSpy = spyOn(component, 'showToast');

      component.confirmSettingsUnpinAll();

      expect(chatService.unpinAllSessions).toHaveBeenCalled();
      expect(closeSpy).toHaveBeenCalled();
      expect(metricsSpy).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith('All chats unpinned successfully!');
    });

    it('should open trash modal on openSettingsTrashDialog', () => {
      const openSpy = jasmine.createSpy('open');
      component.settingsTrashModal = { open: openSpy } as any;

      component.openSettingsTrashDialog();

      expect(openSpy).toHaveBeenCalled();
    });

    it('should handle session restored from trash modal and reload metrics', () => {
      const metricsSpy = spyOn(component, 'loadChatMetrics');
      const toastSpy = spyOn(component, 'showToast');

      component.onSettingsSessionRestored(123);

      expect(metricsSpy).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith('Chat restored successfully!');
    });

    it('should handle trash emptied from trash modal and reload metrics', () => {
      const metricsSpy = spyOn(component, 'loadChatMetrics');
      const toastSpy = spyOn(component, 'showToast');

      component.onSettingsTrashEmptied();

      expect(metricsSpy).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith('Trash emptied successfully!');
    });

    it('should update trashCount on onSettingsTrashCountChange', () => {
      component.onSettingsTrashCountChange(7);
      expect(component.trashCount).toBe(7);
    });
  });

  describe('Confidentiality Mode', () => {
    // The shipped default floor: encrypted storage, a 30-day window and egress already blocked.
    const defaultFloor: ConfidentialFloor = {
      persistence: 'Encrypted',
      retentionDays: 30,
      disableToolEgress: true,
      disableTitleGeneration: false,
      disablePromptCache: false,
      immediatePurge: false,
      modelGate: 'UserDecides'
    };

    const strictestFloor: ConfidentialFloor = {
      persistence: 'Ephemeral',
      retentionDays: 1,
      disableToolEgress: true,
      disableTitleGeneration: true,
      disablePromptCache: true,
      immediatePurge: true,
      modelGate: 'VerifiedPostureOnly'
    };

    function createWith(settings: Partial<UserAiSettings>) {
      spyOn(settingsService, 'getSettings').and.returnValue(of({
        hasApiKey: true,
        spoilerFreeMode: true,
        confidentialFirstUseNoticeAcknowledged: true,
        ...settings
      } as UserAiSettings));
      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
    }

    it('should populate the confidentiality settings and the floor from getSettings', () => {
      createWith({
        confidentialPersistence: 'Ephemeral',
        confidentialRetentionDays: 7,
        confidentialDisableToolEgress: true,
        confidentialDisableTitleGeneration: true,
        confidentialDisablePromptCache: false,
        confidentialImmediatePurge: false,
        confidentialModelGate: 'AskWhenUnclear',
        confidentialFloor: defaultFloor
      });

      expect(component.confidentialPersistence).toBe('Ephemeral');
      expect(component.confidentialRetentionDays).toBe(7);
      expect(component.confidentialDisablePromptCache).toBeFalse();
      expect(component.confidentialImmediatePurge).toBeFalse();
      expect(component.confidentialModelGate).toBe('AskWhenUnclear');
      expect(component.confidentialFloor).toEqual(defaultFloor);
    });

    it('should raise a value weaker than the floor up to the floor on load', () => {
      createWith({
        confidentialPersistence: 'Plaintext',
        confidentialRetentionDays: 90,
        confidentialDisableToolEgress: false,
        confidentialModelGate: 'UserDecides',
        confidentialFloor: defaultFloor
      });

      expect(component.confidentialPersistence).toBe('Encrypted');
      expect(component.confidentialRetentionDays).toBe(30);
      expect(component.confidentialDisableToolEgress).toBeTrue();
      expect(component.confidentialPromiseWeakened).toBeFalse();
    });

    it('should not offer an option the floor forbids', () => {
      createWith({ confidentialFloor: { ...defaultFloor, modelGate: 'AskWhenUnclear' } });

      expect(component.persistenceOptions.map(o => o.value)).toEqual(['Encrypted', 'Ephemeral']);
      expect(component.modelGateOptions.map(o => o.value)).toEqual(['AskWhenUnclear', 'VerifiedPostureOnly']);
    });

    it('should offer every option when no floor is configured', () => {
      createWith({ confidentialFloor: null });

      expect(component.persistenceOptions.length).toBe(3);
      expect(component.modelGateOptions.length).toBe(3);
      expect(component.maxConfidentialRetentionDays).toBe(component.retentionMaxDays);
    });

    it('should report only the settings the floor has maximised as fixed by an administrator', () => {
      createWith({ confidentialFloor: defaultFloor });

      expect(component.toolEgressFixedByAdmin).toBeTrue();
      expect(component.persistenceFixedByAdmin).toBeFalse();
      expect(component.retentionFixedByAdmin).toBeFalse();
      expect(component.titleGenerationFixedByAdmin).toBeFalse();
      expect(component.promptCacheFixedByAdmin).toBeFalse();
      expect(component.immediatePurgeFixedByAdmin).toBeFalse();
      expect(component.modelGateFixedByAdmin).toBeFalse();
    });

    it('should report every setting as fixed under the strictest floor', () => {
      createWith({ confidentialFloor: strictestFloor });

      expect(component.persistenceFixedByAdmin).toBeTrue();
      expect(component.retentionFixedByAdmin).toBeTrue();
      expect(component.toolEgressFixedByAdmin).toBeTrue();
      expect(component.titleGenerationFixedByAdmin).toBeTrue();
      expect(component.promptCacheFixedByAdmin).toBeTrue();
      expect(component.immediatePurgeFixedByAdmin).toBeTrue();
      expect(component.modelGateFixedByAdmin).toBeTrue();
      expect(component.persistenceOptions.map(o => o.value)).toEqual(['Ephemeral']);
      expect(component.modelGateOptions.map(o => o.value)).toEqual(['VerifiedPostureOnly']);
    });

    it('should report the promise as not kept when storage is readable', () => {
      createWith({ confidentialFloor: null, confidentialPersistence: 'Plaintext', confidentialDisableToolEgress: true });

      expect(component.confidentialPromiseWeakened).toBeTrue();
    });

    it('should report the promise as not kept when tool egress stays allowed', () => {
      createWith({ confidentialFloor: null, confidentialPersistence: 'Encrypted', confidentialDisableToolEgress: false });

      expect(component.confidentialPromiseWeakened).toBeTrue();
    });

    it('should forward the eight confidentiality fields as the seventeenth saveSettings argument', fakeAsync(() => {
      createWith({
        confidentialPersistence: 'Encrypted',
        confidentialRetentionDays: 14,
        confidentialDisableToolEgress: true,
        confidentialDisableTitleGeneration: false,
        confidentialDisablePromptCache: false,
        confidentialImmediatePurge: true,
        confidentialModelGate: 'AskWhenUnclear',
        confidentialFloor: defaultFloor
      });
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.confidentialImmediatePurge = false;
      component.onSettingChange();
      tick();

      expect(saveSpy).toHaveBeenCalled();
      expect(saveSpy.calls.mostRecent().args[16]).toEqual({
        confidentialPersistence: 'Encrypted',
        confidentialRetentionDays: 14,
        confidentialDisableToolEgress: true,
        confidentialDisableTitleGeneration: false,
        confidentialDisablePromptCache: false,
        confidentialImmediatePurge: false,
        confidentialModelGate: 'AskWhenUnclear',
        defaultChatPrivacyMode: 'Standard'
      });
    }));

    it('should populate the default privacy mode for new chats from getSettings', () => {
      createWith({ confidentialFloor: null, defaultChatPrivacyMode: 'Incognito' });

      expect(component.defaultChatPrivacyMode).toBe('Incognito');
    });

    it('should leave the default privacy mode at Standard when the server sends none', () => {
      createWith({ confidentialFloor: null });

      expect(component.defaultChatPrivacyMode).toBe('Standard');
    });

    it('should carry the default privacy mode in the confidentiality payload', () => {
      createWith({ confidentialFloor: null, defaultChatPrivacyMode: 'Confidential' });

      expect(component.confidentialPayload.defaultChatPrivacyMode).toBe('Confidential');
    });

    it('should send the floor-clamped value rather than a weaker one the user still holds', fakeAsync(() => {
      createWith({ confidentialFloor: defaultFloor });
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.confidentialDisableToolEgress = false;
      component.confidentialPersistence = 'Plaintext';
      component.onSettingChange();
      tick();

      const payload = saveSpy.calls.mostRecent().args[16] as any;
      expect(payload.confidentialDisableToolEgress).toBeTrue();
      expect(payload.confidentialPersistence).toBe('Encrypted');
    }));

    it('should reject a retention window longer than the floor and block the save', fakeAsync(() => {
      createWith({ confidentialRetentionDays: 30, confidentialFloor: defaultFloor });
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.confidentialRetentionDays = 90;
      component.onNumberInputBlur();
      tick();

      expect(component.validationErrors['confidentialRetentionDays']).toBeDefined();
      expect(saveSpy).not.toHaveBeenCalled();
    }));

    it('should revert an invalid retention window to the last saved value on navigation', async () => {
      createWith({ confidentialRetentionDays: 30, confidentialFloor: defaultFloor });
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.confidentialRetentionDays = 4000;
      component.onNumberInputChange();

      const canLeave = await component.canDeactivate();

      expect(canLeave).toBeTrue();
      expect(component.confidentialRetentionDays).toBe(30);
      expect(component.validationErrors['confidentialRetentionDays']).toBeUndefined();
      expect(saveSpy).toHaveBeenCalled();
    });

    it('should record the first-use acknowledgement and stop showing the notice', () => {
      createWith({ confidentialFirstUseNoticeAcknowledged: false, confidentialFloor: defaultFloor });
      expect(component.confidentialNoticeAcknowledged).toBeFalse();
      const ackSpy = spyOn(settingsService, 'acknowledgeConfidentialNotice').and.returnValue(of({} as any));

      component.acknowledgeConfidentialNotice();

      expect(ackSpy).toHaveBeenCalled();
      expect(component.confidentialNoticeAcknowledged).toBeTrue();
      expect(component.isAcknowledgingConfidentialNotice).toBeFalse();
    });

    it('should show an inline message and keep the notice when the acknowledgement fails', () => {
      createWith({ confidentialFirstUseNoticeAcknowledged: false, confidentialFloor: defaultFloor });
      spyOn(settingsService, 'acknowledgeConfidentialNotice').and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }))
      );

      component.acknowledgeConfidentialNotice();

      expect(component.confidentialNoticeAcknowledged).toBeFalse();
      expect(component.confidentialNoticeError).toContain('could not be recorded');
    });

    it('should render the seven confidentiality controls and hide the acknowledged notice', async () => {
      createWith({ confidentialFloor: defaultFloor });
      showSection('confidentiality');
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('#confidentialPersistence')).toBeTruthy();
      expect(compiled.querySelector('#confidentialRetentionDays')).toBeTruthy();
      expect(compiled.querySelector('input[name="confidentialDisableToolEgress"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="confidentialDisableTitleGeneration"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="confidentialDisablePromptCache"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="confidentialImmediatePurge"]')).toBeTruthy();
      expect(compiled.querySelector('#confidentialModelGate')).toBeTruthy();
      expect(compiled.querySelector('.confidential-notice')).toBeNull();

      // Acknowledging hides the notice but not what it said: the recap keeps it reachable.
      const recap = compiled.querySelector('.confidential-notice-recap');
      expect(recap).toBeTruthy();
      expect(recap!.querySelector('summary')!.textContent).toContain('What Confidentiality Mode covers');
      expect(recap!.textContent).toContain('Not supported:');
    });

    it('should group the confidentiality controls into four named fieldsets', async () => {
      createWith({ confidentialFloor: defaultFloor });
      showSection('confidentiality');
      await fixture.whenStable();
      fixture.detectChanges();

      const legends = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll('fieldset.gh-fieldset > legend')
      ).map(l => l.textContent?.trim());

      expect(legends).toEqual(['New chats', 'The promise', 'Deletion', 'Provider precautions']);
    });

    it('should state the one-way rule as its own callout', async () => {
      createWith({ confidentialFloor: defaultFloor });
      showSection('confidentiality');
      await fixture.whenStable();
      fixture.detectChanges();

      const oneway = (fixture.nativeElement as HTMLElement).querySelector('.confidential-oneway');
      expect(oneway).toBeTruthy();
      expect(oneway!.querySelector('.alert-heading')!.textContent).toContain('Upgrading is one-way');
      expect(oneway!.textContent).toContain('can never go back');
    });

    it('should split the unacknowledged notice into covered, not covered and not supported', async () => {
      createWith({ confidentialFirstUseNoticeAcknowledged: false, confidentialFloor: defaultFloor });
      showSection('confidentiality');
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const notice = compiled.querySelector('.confidential-notice');

      expect(notice).toBeTruthy();
      expect(compiled.querySelector('.confidential-notice-recap')).toBeNull();

      const leads = Array.from(notice!.querySelectorAll('.confidential-scope-list strong'))
        .map(s => s.textContent?.trim());

      expect(leads).toEqual(['Covered:', 'Not covered:', 'Not supported:']);
    });

    it('should disable a control the floor has fixed and explain why', async () => {
      createWith({ confidentialFloor: strictestFloor });
      showSection('confidentiality');
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const persistence = compiled.querySelector('#confidentialPersistence') as HTMLSelectElement;
      const egress = compiled.querySelector('input[name="confidentialDisableToolEgress"]') as HTMLInputElement;

      expect(persistence.disabled).toBeTrue();
      expect(egress.disabled).toBeTrue();
      expect(compiled.querySelectorAll('.confidential-floor-note').length).toBe(7);
    });
  });

  describe('Outbound Masking', () => {
    // Nothing forced on: every class is the user's own decision.
    const noFloor: DlpFloor = {
      apiKeys: false,
      privateKeys: false,
      tokens: false,
      passwords: false,
      creditCards: false,
      ibans: false,
      ssns: false,
      emails: false,
      phoneNumbers: false
    };

    function createWith(settings: Partial<UserAiSettings>) {
      spyOn(settingsService, 'getSettings').and.returnValue(of({
        hasApiKey: true,
        spoilerFreeMode: true,
        confidentialFirstUseNoticeAcknowledged: true,
        ...settings
      } as UserAiSettings));
      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
    }

    it('should default the seven secret classes on and e-mail and phone off', () => {
      createWith({});

      expect(component.dlp.dlpMaskApiKeys).toBeTrue();
      expect(component.dlp.dlpMaskPrivateKeys).toBeTrue();
      expect(component.dlp.dlpMaskTokens).toBeTrue();
      expect(component.dlp.dlpMaskPasswords).toBeTrue();
      expect(component.dlp.dlpMaskCreditCards).toBeTrue();
      expect(component.dlp.dlpMaskIbans).toBeTrue();
      expect(component.dlp.dlpMaskSsns).toBeTrue();
      expect(component.dlp.dlpMaskEmails).toBeFalse();
      expect(component.dlp.dlpMaskPhoneNumbers).toBeFalse();
      expect(component.dlpFloor).toBeNull();
    });

    it('should populate every class from the resolved values getSettings returns', () => {
      createWith({
        dlpMaskApiKeys: false,
        dlpMaskPrivateKeys: false,
        dlpMaskTokens: true,
        dlpMaskPasswords: false,
        dlpMaskCreditCards: false,
        dlpMaskIbans: true,
        dlpMaskSsns: false,
        dlpMaskEmails: true,
        dlpMaskPhoneNumbers: true,
        dlpFloor: noFloor
      });

      expect(component.dlp.dlpMaskApiKeys).toBeFalse();
      expect(component.dlp.dlpMaskPrivateKeys).toBeFalse();
      expect(component.dlp.dlpMaskTokens).toBeTrue();
      expect(component.dlp.dlpMaskPasswords).toBeFalse();
      expect(component.dlp.dlpMaskCreditCards).toBeFalse();
      expect(component.dlp.dlpMaskIbans).toBeTrue();
      expect(component.dlp.dlpMaskSsns).toBeFalse();
      expect(component.dlp.dlpMaskEmails).toBeTrue();
      expect(component.dlp.dlpMaskPhoneNumbers).toBeTrue();
    });

    it('should raise a class the operator forces on even when the user preference is off', () => {
      createWith({
        dlpMaskEmails: false,
        dlpFloor: { ...noFloor, emails: true }
      });

      expect(component.dlp.dlpMaskEmails).toBeTrue();
      expect(component.effectiveDlp('dlpMaskEmails')).toBeTrue();
      expect(component.isDlpFixedByAdmin('dlpMaskEmails')).toBeTrue();
      expect(component.isDlpFixedByAdmin('dlpMaskPhoneNumbers')).toBeFalse();
    });

    it('should render all nine switches', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('input[name="dlpMaskApiKeys"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskPrivateKeys"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskTokens"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskPasswords"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskCreditCards"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskIbans"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskSsns"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskEmails"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskPhoneNumbers"]')).toBeTruthy();
      expect(compiled.querySelectorAll('.dlp-floor-note').length).toBe(0);
    });

    it('should group the masking switches into two named fieldsets', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const legends = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll('fieldset.gh-fieldset > legend')
      ).map(l => l.textContent?.trim());

      expect(legends).toEqual(['Credentials and payment data', 'Contact details']);
    });

    it('should say that only the US format of an ID number is recognised', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const hint = (fixture.nativeElement as HTMLElement).querySelector('#dlpMaskSsnsHint');
      expect(hint!.textContent).toContain('ID numbers from other countries are not recognised');
    });

    it('should reflect the defaults in the rendered checkboxes', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const keys = compiled.querySelector('input[name="dlpMaskApiKeys"]') as HTMLInputElement;
      const emails = compiled.querySelector('input[name="dlpMaskEmails"]') as HTMLInputElement;

      expect(keys.checked).toBeTrue();
      expect(keys.disabled).toBeFalse();
      expect(emails.checked).toBeFalse();
      expect(emails.disabled).toBeFalse();
    });

    it('should render a forced class as on and disabled, with the operator note', async () => {
      createWith({ dlpMaskPhoneNumbers: false, dlpFloor: { ...noFloor, phoneNumbers: true } });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const phones = compiled.querySelector('input[name="dlpMaskPhoneNumbers"]') as HTMLInputElement;

      expect(phones.checked).toBeTrue();
      expect(phones.disabled).toBeTrue();
      expect(phones.getAttribute('aria-describedby')).toContain('dlpMaskPhoneNumbersFloor');

      const notes = compiled.querySelectorAll('.dlp-floor-note');
      expect(notes.length).toBe(1);
      expect(notes[0].textContent).toContain('An administrator requires this class to be masked.');
      expect(notes[0].id).toBe('dlpMaskPhoneNumbersFloor');
    });

    it('should state plainly that masking is not a guarantee', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const limit = (fixture.nativeElement as HTMLElement).querySelector('.dlp-limit');
      expect(limit).toBeTruthy();
      // The limit carries a real heading, so it does not read as one more grey hint.
      expect(limit!.querySelector('.alert-heading')!.textContent).toContain('A safety net, not a guarantee');
      expect(limit!.textContent).toContain('it does not guarantee it');
      expect(limit!.textContent).toContain('A secret with no recognisable shape passes straight through');
    });

    it('should present how masking works as its own callout with three steps', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const how = (fixture.nativeElement as HTMLElement).querySelector('.dlp-how');
      expect(how).toBeTruthy();
      expect(how!.querySelector('.alert-heading')!.textContent).toContain('How it works');
      expect(how!.querySelectorAll('.dlp-steps li').length).toBe(3);
      expect(how!.textContent).toContain('the provider only ever receives the placeholder');
    });

    it('should say that masking applies to every chat, not only a confidential one', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const intro = Array.from(compiled.querySelectorAll('.dlp-intro'))
        .map(p => p.textContent ?? '')
        .join(' ');

      expect(intro).toContain('every outbound turn, in every chat, confidential or not');
    });

    it('should explain why e-mail addresses and phone numbers start off', async () => {
      createWith({ dlpFloor: noFloor });
      showSection('masking');
      await fixture.whenStable();
      fixture.detectChanges();

      const note = (fixture.nativeElement as HTMLElement).querySelector('.dlp-default-note');
      expect(note).toBeTruthy();
      expect(note!.textContent).toContain('measurably degrades answers');
    });

    it('should not post masking settings during the initial load', fakeAsync(() => {
      createWith({ dlpFloor: noFloor });
      const dlpSpy = spyOn(settingsService, 'saveDlpSettings').and.returnValue(of({} as any));

      tick(1000);

      expect(dlpSpy).not.toHaveBeenCalled();
    }));

    it('should post all nine classes to the DLP endpoint as soon as one is switched', fakeAsync(() => {
      createWith({ dlpFloor: noFloor });
      const dlpSpy = spyOn(settingsService, 'saveDlpSettings').and.returnValue(of({} as any));

      component.dlp.dlpMaskEmails = true;
      component.onDlpChange();
      tick();

      expect(dlpSpy).toHaveBeenCalledTimes(1);
      expect(dlpSpy.calls.mostRecent().args[0]).toEqual({
        dlpMaskApiKeys: true,
        dlpMaskPrivateKeys: true,
        dlpMaskTokens: true,
        dlpMaskPasswords: true,
        dlpMaskCreditCards: true,
        dlpMaskIbans: true,
        dlpMaskSsns: true,
        dlpMaskEmails: true,
        dlpMaskPhoneNumbers: false
      });
      expect(component.saveState).toBe('saved');
    }));

    it('should not post masking settings through the main settings save', fakeAsync(() => {
      createWith({ dlpFloor: noFloor });
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));
      spyOn(settingsService, 'saveDlpSettings').and.returnValue(of({} as any));

      component.dlp.dlpMaskEmails = true;
      component.onDlpChange();
      tick();

      expect(saveSpy).not.toHaveBeenCalled();
    }));

    it('should send the forced value rather than the weaker one the user still holds', fakeAsync(() => {
      createWith({ dlpFloor: { ...noFloor, ssns: true } });
      const dlpSpy = spyOn(settingsService, 'saveDlpSettings').and.returnValue(of({} as any));

      component.dlp.dlpMaskSsns = false;
      component.onDlpChange();
      tick();

      const payload = dlpSpy.calls.mostRecent().args[0] as any;
      expect(payload.dlpMaskSsns).toBeTrue();
    }));

    it('should report a failed masking save and keep the pipeline alive', fakeAsync(() => {
      createWith({ dlpFloor: noFloor });
      const dlpSpy = spyOn(settingsService, 'saveDlpSettings').and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }))
      );

      component.dlp.dlpMaskEmails = true;
      component.onDlpChange();
      tick();

      expect(dlpSpy).toHaveBeenCalledTimes(1);
      expect(component.saveState).toBe('error');

      dlpSpy.and.returnValue(of({} as any));
      component.dlp.dlpMaskEmails = false;
      component.onDlpChange();
      tick();

      expect(dlpSpy).toHaveBeenCalledTimes(2);
      expect(component.saveState).toBe('saved');
    }));

    it('should not fire the main settings save when only a masking class changed', async () => {
      createWith({ dlpFloor: noFloor });
      const dlpSpy = spyOn(settingsService, 'saveDlpSettings').and.returnValue(of({} as any));
      const saveSpy = spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Saved' } as any));

      component.dlp.dlpMaskPhoneNumbers = true;
      component.onDlpChange();

      const canLeave = await component.canDeactivate();

      expect(canLeave).toBeTrue();
      expect(dlpSpy).toHaveBeenCalledTimes(1);
      expect(saveSpy).not.toHaveBeenCalled();
    });

    it('canDeactivate should await an in-flight masking save', async () => {
      createWith({ dlpFloor: noFloor });
      const dlpSubject = new Subject<any>();
      spyOn(settingsService, 'saveDlpSettings').and.returnValue(dlpSubject.asObservable());

      component.dlp.dlpMaskPhoneNumbers = true;
      component.onDlpChange();

      let resolved = false;
      const canDeactivatePromise = component.canDeactivate().then((res) => {
        resolved = true;
        return res;
      });

      expect(resolved).toBeFalse();

      dlpSubject.next({});
      dlpSubject.complete();

      const result = await canDeactivatePromise;
      expect(result).toBeTrue();
      expect(resolved).toBeTrue();
    });
  });

  describe('Sections', () => {
    let paramMapSubject: Subject<ParamMap>;

    beforeEach(async () => {
      TestBed.resetTestingModule();
      paramMapSubject = new Subject<ParamMap>();

      await TestBed.configureTestingModule({
        imports: [SettingsComponent],
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: ActivatedRoute, useValue: { paramMap: paramMapSubject.asObservable() } }
        ]
      }).compileComponents();

      settingsService = TestBed.inject(SettingsService);
      spyOn(settingsService, 'getSettings').and.returnValue(of({ hasApiKey: true } as any));

      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
    });

    function emitSection(section: string | null) {
      paramMapSubject.next(convertToParamMap(section ? { section } : {}));
      fixture.detectChanges();
    }

    it('renders eight nav links with the expected labels and routerLinks', () => {
      const compiled = fixture.nativeElement as HTMLElement;
      const links = compiled.querySelectorAll('.settings-nav-link');
      const expectedLabels = ['General', 'AI Permissions', 'AI Performance', 'Confidentiality Mode', 'Provided Models for Confidential Chats', 'Outbound Masking', 'Chat Data', 'Version'];

      expect(links.length).toBe(8);
      links.forEach((link, i) => {
        expect(link.querySelector('.settings-nav-label')?.textContent).toBe(expectedLabels[i]);
      });
    });

    it('marks aria-current="page" on General when the URL names no section', () => {
      emitSection(null);
      const compiled = fixture.nativeElement as HTMLElement;
      const current = compiled.querySelector('.settings-nav-link[aria-current="page"]');
      expect(current?.querySelector('.settings-nav-label')?.textContent).toBe('General');
    });

    it('renders the version row and Release Notes button in the Version section', () => {
      emitSection('version');
      const compiled = fixture.nativeElement as HTMLElement;
      const row = compiled.querySelector('.version-row');

      expect(row).toBeTruthy();
      expect(row!.querySelector('.version-notes-btn')?.textContent?.trim()).toContain('Release Notes');
      expect(compiled.querySelector('fieldset.version-fieldset')).toBeNull();
    });

    it('does not render the version row in the General section', () => {
      emitSection(null);
      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.version-row')).toBeNull();
    });

    it('a paramMap emitting version sets activeSection and the header label', () => {
      emitSection('version');
      expect(component.activeSection).toBe('version');
      expect(component.contentSectionLabel).toBe('Version');
    });

    it('renders no sparkle badge on the Release Notes button', () => {
      emitSection('version');
      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.version-notes-btn .sparkle-icon')).toBeNull();
    });

    it('renders the default-for-new-chats select first in the Confidentiality section', () => {
      emitSection('confidentiality');
      const compiled = fixture.nativeElement as HTMLElement;
      const select = compiled.querySelector('#defaultChatPrivacyMode');

      expect(select).toBeTruthy();
      expect(select!.querySelectorAll('option').length).toBe(3);
    });

    it('a paramMap emitting confidentiality sets activeSection and renders the Storage select', () => {
      emitSection('confidentiality');
      expect(component.activeSection).toBe('confidentiality');
      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('#confidentialPersistence')).toBeTruthy();
    });

    it('falls back to null / General for an unknown section value', () => {
      emitSection('nonsense');
      expect(component.activeSection).toBeNull();
      expect(component.contentSection).toBe('general');
    });

    it('.settings-body carries section-open only when a section is named', () => {
      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.settings-body')?.classList.contains('section-open')).toBeFalse();

      emitSection('masking');
      expect(compiled.querySelector('.settings-body')?.classList.contains('section-open')).toBeTrue();
    });
  });

  describe('Provided Models for Confidential Chats', () => {
    let paramMapSubject: Subject<ParamMap>;

    // One verified model still undecided, one self-declared model already accepted.
    const providedModels: ProvidedModelConfidentialStatus[] = [
      {
        id: 7,
        provider: 'Anthropic',
        modelId: 'claude-house',
        displayName: 'House Claude',
        posture: 'ZeroRetention',
        postureText: 'Zero data retention',
        postureVerifiedUtc: '2026-03-04T10:00:00Z',
        isOperatorVerified: true,
        dataRegion: 'eu-west',
        note: null,
        userTrustsForConfidential: null,
        decidedUtc: null
      },
      {
        id: 9,
        provider: 'Google',
        modelId: 'gemini-house',
        displayName: 'House Gemini',
        posture: 'NoTraining',
        postureText: 'No training on content',
        postureVerifiedUtc: null,
        isOperatorVerified: false,
        dataRegion: null,
        note: null,
        userTrustsForConfidential: true,
        decidedUtc: '2026-03-05T10:00:00Z'
      }
    ];

    beforeEach(async () => {
      TestBed.resetTestingModule();
      paramMapSubject = new Subject<ParamMap>();

      await TestBed.configureTestingModule({
        imports: [SettingsComponent],
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: ActivatedRoute, useValue: { paramMap: paramMapSubject.asObservable() } }
        ]
      }).compileComponents();

      settingsService = TestBed.inject(SettingsService);
      spyOn(settingsService, 'getSettings').and.returnValue(of({ hasApiKey: true } as any));
      spyOn(settingsService, 'getProvidedModelsForConfidential').and.returnValue(
        of(providedModels.map(m => ({ ...m })))
      );

      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
    });

    /** Enters the section the way the route does, which is what triggers the fetch. */
    async function enterSection() {
      paramMapSubject.next(convertToParamMap({ section: 'provided-models' }));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    /* The rendered text of a row's chosen option. Angular prefixes a select's value with the
       option's internal id, so the text is what identifies the selection. */
    function chosenText(id: number): string {
      const select = (fixture.nativeElement as HTMLElement).querySelector(`#provided-trust-${id}`) as HTMLSelectElement;
      return select.options[select.selectedIndex]?.textContent?.trim() ?? '';
    }

    it('lists the provided models the service returns', async () => {
      await enterSection();

      const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('table.gh-table tbody tr');

      expect(rows.length).toBe(2);
      expect(rows[0].querySelector('th')?.textContent?.trim()).toBe('House Claude');
      expect(rows[0].textContent).toContain('Anthropic');
      expect(rows[0].textContent).toContain('Zero data retention');
      expect(rows[0].textContent).toContain('verified');
      expect(rows[1].querySelector('th')?.textContent?.trim()).toBe('House Gemini');
      expect(rows[1].textContent).toContain('self-declared');
    });

    it('offers each row the three decisions and shows the one on record', async () => {
      await enterSection();

      const select = (fixture.nativeElement as HTMLElement).querySelector('#provided-trust-7') as HTMLSelectElement;

      expect(select.querySelectorAll('option').length).toBe(3);
      expect(chosenText(7)).toBe('Not decided yet');
      expect(chosenText(9)).toBe('Yes');
    });

    it('saves a changed decision with that row id and value, undecided included', async () => {
      const trustSpy = spyOn(settingsService, 'setProvidedModelConfidentialTrust').and.returnValue(of({} as any));
      await enterSection();

      component.onProvidedModelTrustChange(component.providedModels[0], 'yes');
      expect(trustSpy).toHaveBeenCalledWith(7, true);

      component.onProvidedModelTrustChange(component.providedModels[0], 'no');
      expect(trustSpy).toHaveBeenCalledWith(7, false);

      component.onProvidedModelTrustChange(component.providedModels[1], 'undecided');
      expect(trustSpy).toHaveBeenCalledWith(9, null);
      expect(component.providedModelTrusts[9]).toBe('undecided');
      expect(component.providedModels[1].userTrustsForConfidential).toBeNull();
      expect(component.providedModels[1].decidedUtc).toBeNull();
    });

    it('leaves a row on its stored decision and shows the error when the save fails', async () => {
      spyOn(settingsService, 'setProvidedModelConfidentialTrust').and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 404, error: { error: 'Model not found.' } }))
      );
      await enterSection();

      component.onProvidedModelTrustChange(component.providedModels[1], 'no');
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(component.providedModelTrusts[9]).toBe('yes');
      expect(component.providedModels[1].userTrustsForConfidential).toBeTrue();

      const compiled = fixture.nativeElement as HTMLElement;
      const errors = compiled.querySelectorAll('[role="alert"]');
      expect(errors.length).toBe(1);
      expect(errors[0].textContent).toContain('Model not found.');
      expect(chosenText(9)).toBe('Yes');
    });
  });
});
