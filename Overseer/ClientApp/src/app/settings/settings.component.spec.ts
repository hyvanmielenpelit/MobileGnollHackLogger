import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError, Subject } from 'rxjs';
import { SettingsComponent } from './settings.component';
import { SettingsService, UserAiSettings } from '../services/settings.service';
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
});
