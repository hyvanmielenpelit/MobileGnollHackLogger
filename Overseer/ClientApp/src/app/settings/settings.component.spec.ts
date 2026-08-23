import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
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
        enableClientTools: false,
        enableGameActions: true,
        maxResultLength: 5000,
        maxCallsPerSession: 10,
        maxToolIterations: 3,
        requestTimeout: 60
      };
      spyOn(settingsService, 'getSettings').and.returnValue(of(mockSettings));

      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.spoilerFreeMode).toBeFalse();
      expect(component.showSourceCodeReferences).toBeTrue();
      expect(component.showThoughtsAndTools).toBe(1);
      expect(component.enableClientTools).toBeFalse();
      expect(component.enableGameActions).toBeTrue();
      expect(component.maxResultLength).toBe(5000);
      expect(component.maxCallsPerSession).toBe(10);
      expect(component.maxToolIterations).toBe(3);
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

  describe('saveSettings', () => {
    beforeEach(() => {
      spyOn(settingsService, 'getSettings').and.returnValue(of({
        hasApiKey: true,
        spoilerFreeMode: true,
        showThoughtsAndTools: 0
      }));
      fixture = TestBed.createComponent(SettingsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
    });

    it('should reset loading to false and emit updated thoughts setting on normal success', () => {
      spyOn(settingsService, 'saveSettings').and.returnValue(of({ message: 'Settings saved' }));
      const thoughtsSpy = spyOn(settingsService.showThoughtsAndToolsUpdated, 'next');

      component.showThoughtsAndTools = 2;
      component.saveSettings();

      expect(component.loading).toBeFalse();
      expect(thoughtsSpy).toHaveBeenCalledWith(2);
    });

    it('should catch TypeError: Failed to fetch and reset loading to false', () => {
      spyOn(settingsService, 'saveSettings').and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      component.saveSettings();

      expect(component.loading).toBeFalse();
    });

    it('should catch HttpErrorResponse 500 and reset loading to false', () => {
      spyOn(settingsService, 'saveSettings').and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }))
      );

      component.saveSettings();

      expect(component.loading).toBeFalse();
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
