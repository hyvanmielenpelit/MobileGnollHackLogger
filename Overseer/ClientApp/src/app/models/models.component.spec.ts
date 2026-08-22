import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { ModelsComponent } from './models.component';
import { SettingsService } from '../services/settings.service';

describe('ModelsComponent', () => {
  let component: ModelsComponent;
  let fixture: ComponentFixture<ModelsComponent>;
  let settingsService: SettingsService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ModelsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    })
    .compileComponents();

    settingsService = TestBed.inject(SettingsService);
    spyOn(settingsService, 'getUserModels').and.returnValue(of([]));
    spyOn(settingsService, 'getSettings').and.returnValue(of({
      hasApiKey: true,
      spoilerFreeMode: true
    }));

    fixture = TestBed.createComponent(ModelsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('confirmDelete', () => {
    let mockDialog: any;

    beforeEach(() => {
      mockDialog = {
        showModal: jasmine.createSpy('showModal'),
        close: jasmine.createSpy('close')
      };
      component.deleteModelConfirmDialog = { nativeElement: mockDialog };
    });

    it('should delete model and reload settings on normal success', () => {
      component.modelToDeleteId = 42;
      component.titleModelSelection = 'u_42';
      
      const deleteSpy = spyOn(settingsService, 'deleteUserModel').and.returnValue(of({ message: 'Deleted' }));
      (settingsService.getSettings as jasmine.Spy).and.returnValue(of({
        hasApiKey: true,
        spoilerFreeMode: true,
        titleGenerationModelId: 99
      }));

      component.confirmDelete();

      expect(deleteSpy).toHaveBeenCalledWith(42);
      expect(component.saving).toBeFalse();
      expect(component.titleModelSelection).toBe('u_99');
      expect(mockDialog.close).toHaveBeenCalled();
    });

    it('should handle inner getSettings failure (TypeError: Failed to fetch) gracefully after model deletion', () => {
      component.modelToDeleteId = 42;
      
      spyOn(settingsService, 'deleteUserModel').and.returnValue(of({ message: 'Deleted' }));
      (settingsService.getSettings as jasmine.Spy).and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      expect(() => {
        component.confirmDelete();
      }).not.toThrow();

      expect(component.saving).toBeFalse();
      expect(mockDialog.close).toHaveBeenCalled();
    });

    it('should catch TypeError: Failed to fetch on deleteUserModel, close modal, and reset saving to false', () => {
      component.modelToDeleteId = 42;

      spyOn(settingsService, 'deleteUserModel').and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      expect(() => {
        component.confirmDelete();
      }).not.toThrow();

      expect(component.saving).toBeFalse();
      expect(mockDialog.close).toHaveBeenCalled();
    });
  });
});
