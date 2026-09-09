import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError, Subject } from 'rxjs';
import { SettingsComponent } from './settings.component';
import { SettingsService, UserAiSettings, ConfidentialFloor, DlpFloor } from '../services/settings.service';
import { ChatService } from '../services/chat.service';

describe('SettingsComponent', () => {
  let component: SettingsComponent;
  let fixture: ComponentFixture<SettingsComponent>;
  let settingsService: SettingsService;

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

    it('should forward the seven confidentiality fields as the seventeenth saveSettings argument', fakeAsync(() => {
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
        confidentialModelGate: 'AskWhenUnclear'
      });
    }));

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
    });

    it('should disable a control the floor has fixed and explain why', async () => {
      createWith({ confidentialFloor: strictestFloor });
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
      creditCards: false,
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

    it('should default the five secret classes on and e-mail and phone off', () => {
      createWith({});

      expect(component.dlp.dlpMaskApiKeys).toBeTrue();
      expect(component.dlp.dlpMaskPrivateKeys).toBeTrue();
      expect(component.dlp.dlpMaskTokens).toBeTrue();
      expect(component.dlp.dlpMaskCreditCards).toBeTrue();
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
        dlpMaskCreditCards: false,
        dlpMaskSsns: false,
        dlpMaskEmails: true,
        dlpMaskPhoneNumbers: true,
        dlpFloor: noFloor
      });

      expect(component.dlp.dlpMaskApiKeys).toBeFalse();
      expect(component.dlp.dlpMaskPrivateKeys).toBeFalse();
      expect(component.dlp.dlpMaskTokens).toBeTrue();
      expect(component.dlp.dlpMaskCreditCards).toBeFalse();
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

    it('should render all seven switches', async () => {
      createWith({ dlpFloor: noFloor });
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('input[name="dlpMaskApiKeys"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskPrivateKeys"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskTokens"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskCreditCards"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskSsns"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskEmails"]')).toBeTruthy();
      expect(compiled.querySelector('input[name="dlpMaskPhoneNumbers"]')).toBeTruthy();
      expect(compiled.querySelectorAll('.dlp-floor-note').length).toBe(0);
    });

    it('should reflect the defaults in the rendered checkboxes', async () => {
      createWith({ dlpFloor: noFloor });
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
      await fixture.whenStable();
      fixture.detectChanges();

      const limit = (fixture.nativeElement as HTMLElement).querySelector('.dlp-limit');
      expect(limit).toBeTruthy();
      expect(limit!.textContent).toContain('it does not guarantee it');
      expect(limit!.textContent).toContain('A secret with no recognisable shape passes straight through');
    });

    it('should say that masking applies to every chat, not only a confidential one', async () => {
      createWith({ dlpFloor: noFloor });
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

    it('should post all seven classes to the DLP endpoint as soon as one is switched', fakeAsync(() => {
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
        dlpMaskCreditCards: true,
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
});
