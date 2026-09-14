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

  it('should load parallel execution mode and save on change', () => {
    spyOn(settingsService, 'getApiKeys').and.returnValue(of([
      { provider: 'OpenAI', hasKey: true, parallelExecutionMode: 0 },
      { provider: 'Anthropic', hasKey: false, parallelExecutionMode: 1 }
    ]));
    const saveModeSpy = spyOn(settingsService, 'saveApiKeyParallelMode').and.returnValue(of({}));

    component.loadStatuses();

    expect(component.keyParallelModes['OpenAI']).toBe(0);
    expect(component.keyParallelModes['Anthropic']).toBe(1);

    component.onParallelModeChange('OpenAI', 2);
    expect(saveModeSpy).toHaveBeenCalledWith('OpenAI', 2);
    expect(component.keyParallelModes['OpenAI']).toBe(2);
    expect(component.savingParallelProvider).toBe('');
  });

  it('should report no advanced summary when every setting is at its default', () => {
    spyOn(settingsService, 'getApiKeys').and.returnValue(of([
      {
        provider: 'OpenAI', hasKey: true, parallelExecutionMode: 2,
        confidentialityPosture: 'Unknown', userTrustsForConfidential: null
      }
    ]));

    component.loadStatuses();

    expect(component.advancedSummary('OpenAI')).toBe('');
  });

  it('should list the non-default advanced settings in the summary', () => {
    spyOn(settingsService, 'getApiKeys').and.returnValue(of([
      {
        provider: 'OpenAI', hasKey: true, parallelExecutionMode: 0,
        confidentialityPosture: 'Unknown', userTrustsForConfidential: false
      }
    ]));

    component.loadStatuses();

    expect(component.advancedSummary('OpenAI')).toBe('Sequential only \u00b7 Not for confidential chats');
  });

  it('should offer a personal key only the postures it can describe', () => {
    spyOn(settingsService, 'getApiKeys').and.returnValue(of([
      { provider: 'OpenAI', hasKey: true, confidentialityPosture: 'Unknown' },
      { provider: 'Google', hasKey: true, confidentialityPosture: 'SelfHosted' }
    ]));

    component.loadStatuses();

    const offered = component.postureOptions('OpenAI').map(p => p.value);
    expect(offered).toEqual(['Unknown', 'Standard', 'NoTraining', 'ZeroRetention']);

    // A legacy row keeps showing what it holds, so the user can change away from it.
    expect(component.postureOptions('Google').map(p => p.value)).toContain('SelfHosted');
  });

  it('should name the saved posture in the badge, and an undeclared one as an absence', () => {
    spyOn(settingsService, 'getApiKeys').and.returnValue(of([
      { provider: 'OpenAI', hasKey: true, confidentialityPosture: 'Unknown' },
      { provider: 'Google', hasKey: true, confidentialityPosture: 'NoTraining' }
    ]));

    component.loadStatuses();

    expect(component.savedPostureBadge('OpenAI')).toBe('Not recorded');
    expect(component.savedPostureBadge('Google')).toBe('Self-declared: No training on content');
  });

  it('should open the About dialog from the summary without toggling the disclosure', () => {
    spyOn(settingsService, 'getApiKeys').and.returnValue(of([
      { provider: 'OpenAI', hasKey: true }
    ]));

    component.loadStatuses();
    fixture.detectChanges();

    const openSpy = spyOn(component, 'openAdvancedInfo');
    const disclosure: HTMLDetailsElement =
      fixture.nativeElement.querySelector('details.advanced-settings');
    const infoButton = disclosure.querySelector('summary .btn-info') as HTMLButtonElement;

    infoButton.click();
    fixture.detectChanges();

    expect(openSpy).toHaveBeenCalled();
    expect(disclosure.open).toBeFalse();
  });

  it('should render the advanced settings disclosure closed', () => {
    spyOn(settingsService, 'getApiKeys').and.returnValue(of([
      { provider: 'OpenAI', hasKey: true }
    ]));

    component.loadStatuses();
    fixture.detectChanges();

    const disclosures: HTMLDetailsElement[] =
      Array.from(fixture.nativeElement.querySelectorAll('details.advanced-settings'));
    expect(disclosures.length).toBeGreaterThan(0);
    for (const disclosure of disclosures) {
      expect(disclosure.open).toBeFalse();
    }
  });
});
