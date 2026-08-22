import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { SettingsComponent } from './settings.component';
import { SettingsService, UserAiSettings } from '../services/settings.service';

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
});
