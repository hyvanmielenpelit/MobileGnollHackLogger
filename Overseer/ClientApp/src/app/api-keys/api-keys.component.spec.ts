import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiKeysComponent } from './api-keys.component';
import { SettingsService } from '../services/settings.service';
import { of } from 'rxjs';

describe('ApiKeysComponent', () => {
  let component: ApiKeysComponent;
  let fixture: ComponentFixture<ApiKeysComponent>;
  let settingsService: SettingsService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ApiKeysComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ApiKeysComponent);
    component = fixture.componentInstance;
    settingsService = TestBed.inject(SettingsService);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should open delete confirm dialog when requestDeleteKey is called', () => {
    const dialogEl = document.createElement('dialog');
    spyOn(dialogEl, 'showModal');
    component.deleteConfirmDialog = { nativeElement: dialogEl };

    component.requestDeleteKey('OpenAI');

    expect(component.deletingProvider).toBe('OpenAI');
    expect(dialogEl.showModal).toHaveBeenCalled();
  });

  it('should close delete confirm dialog and reset deletingProvider when closeDeleteConfirmDialog is called', () => {
    const dialogEl = document.createElement('dialog');
    spyOn(dialogEl, 'close');
    component.deleteConfirmDialog = { nativeElement: dialogEl };
    component.deletingProvider = 'OpenAI';

    component.closeDeleteConfirmDialog();

    expect(component.deletingProvider).toBeNull();
    expect(dialogEl.close).toHaveBeenCalled();
  });

  it('should call deleteApiKeyForProvider and update key status when confirmDeleteKey is called', () => {
    const dialogEl = document.createElement('dialog');
    spyOn(dialogEl, 'close');
    component.deleteConfirmDialog = { nativeElement: dialogEl };
    component.deletingProvider = 'Anthropic';
    component.keyStatuses['Anthropic'] = true;

    spyOn(settingsService, 'deleteApiKeyForProvider').and.returnValue(of({}));

    component.confirmDeleteKey();

    expect(dialogEl.close).toHaveBeenCalled();
    expect(settingsService.deleteApiKeyForProvider).toHaveBeenCalledWith('Anthropic');
    expect(component.keyStatuses['Anthropic']).toBeFalse();
    expect(component.savingProvider).toBe('');
  });
});
