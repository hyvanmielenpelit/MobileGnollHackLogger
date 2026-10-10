import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { ModelsComponent, SYSTEM_MODEL_ATTENTION_TEXT } from './models.component';
import { SettingsService, UserAiModel } from '../services/settings.service';
import { ModelAvailability, ModelResolutionResult } from '../shared/model-availability/model-availability';

const RETIRED: ModelAvailability = {
  status: 'retired',
  needsAttention: true,
  retiredOn: '2026-09-30',
  replacement: { modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash' }
};
const NOT_IN_CATALOG: ModelAvailability = { status: 'notInCatalog', needsAttention: true };
const AVAILABLE: ModelAvailability = { status: 'available', needsAttention: false };
const CUSTOM: ModelAvailability = { status: 'custom', needsAttention: false };

function flash(availability: ModelAvailability, overrides: Partial<UserAiModel> = {}): UserAiModel {
  return { id: 5, provider: 'Google', modelId: 'gemini-3.7-flash', displayName: 'Flash 3.7', modelAvailability: availability, ...overrides };
}

function gptX(availability: ModelAvailability): UserAiModel {
  return { id: 7, provider: 'OpenAI', modelId: 'gpt-x', displayName: 'GPT X', modelAvailability: availability };
}

const TUNED: UserAiModel = {
  id: 6, provider: 'Google', modelId: 'my-tuned-model', displayName: 'Tuned', modelAvailability: CUSTOM, modelCatalogMode: 'custom'
};

const OLD_SYSTEM: UserAiModel = {
  id: 40, provider: 'Google', modelId: 'gemini-old', displayName: 'Old System', isSystem: true, modelAvailability: RETIRED
};

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
    vi.spyOn(settingsService, 'getUserModels').mockReturnValue(of([]));
    vi.spyOn(settingsService, 'getSettings').mockReturnValue(of({
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
        showModal: vi.fn().mockName('showModal'),
        close: vi.fn().mockName('close')
      };
      component.deleteModelConfirmDialog = { nativeElement: mockDialog };
    });

    it('should delete model and reload settings on normal success', () => {
      component.modelToDeleteId = 42;
      component.titleModelSelection = 'u_42';
      
      const deleteSpy = vi.spyOn(settingsService, 'deleteUserModel').mockReturnValue(of({ message: 'Deleted' }));
      (settingsService.getSettings as Mock).mockReturnValue(of({
        hasApiKey: true,
        spoilerFreeMode: true,
        titleGenerationModelId: 99
      }));

      component.confirmDelete();

      expect(deleteSpy).toHaveBeenCalledWith(42);
      expect(component.saving).toBe(false);
      expect(component.titleModelSelection).toBe('u_99');
      expect(mockDialog.close).toHaveBeenCalled();
    });

    it('should handle inner getSettings failure (TypeError: Failed to fetch) gracefully after model deletion', () => {
      component.modelToDeleteId = 42;
      
      vi.spyOn(settingsService, 'deleteUserModel').mockReturnValue(of({ message: 'Deleted' }));
      (settingsService.getSettings as Mock).mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        component.confirmDelete();
      }).not.toThrow();

      expect(component.saving).toBe(false);
      expect(mockDialog.close).toHaveBeenCalled();
    });

    it('should catch TypeError: Failed to fetch on deleteUserModel, close modal, and reset saving to false', () => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      component.modelToDeleteId = 42;

      vi.spyOn(settingsService, 'deleteUserModel').mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        component.confirmDelete();
      }).not.toThrow();

      expect(component.saving).toBe(false);
      expect(mockDialog.close).toHaveBeenCalled();
      expect(consoleError).toHaveBeenCalledWith('Failed to delete model', expect.any(TypeError));
    });
  });

  describe('onEditSave', () => {
    let mockEditDialog: any;

    beforeEach(() => {
      mockEditDialog = {
        showModal: vi.fn().mockName('showModal'),
        close: vi.fn().mockName('close')
      };
      component.editModelDialog = { nativeElement: mockEditDialog };
    });

    it('should call updateUserModel with updated modelId and provider', () => {
      component.editingModel = {
        id: 10,
        provider: 'Google',
        modelId: 'gemini-3.6-flash',
        displayName: 'Gemini 3.6 Flash'
      };

      const updateSpy = vi.spyOn(settingsService, 'updateUserModel').mockReturnValue(of({}));
      vi.spyOn(component, 'loadModels').mockReturnValue(undefined);

      component.onEditSave({
        displayName: 'Gemini 3.7 Flash',
        displayNameMode: 'model_name',
        provider: 'Google',
        modelId: 'gemini-3.7-flash',
        thinkingLevel: 'high',
        reasoningMode: null,
        reasoningSummary: null,
        serviceTier: null,
        maxInputTokens: null,
        maxOutputTokens: null
      });

      expect(updateSpy).toHaveBeenCalledWith(
        10,
        'Gemini 3.7 Flash',
        'model_name',
        'high',
        undefined,
        undefined,
        undefined,
        null,
        null,
        'gemini-3.7-flash',
        'Google'
      );
      expect(component.loadModels).toHaveBeenCalled();
      expect(mockEditDialog.close).toHaveBeenCalled();
      expect(component.saving).toBe(false);
      expect(component.editingModel).toBeNull();
    });
  });

  describe('catalog availability', () => {
    const el = () => fixture.nativeElement as HTMLElement;
    const text = (node: Element | null | undefined) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
    const userRow = (id: number) => el().querySelector(`#user-model-name-${id}`)!.closest('.model-item') as HTMLElement;
    const systemRow = () => el().querySelector('.system-model-item') as HTMLElement;
    const resolveButton = (id: number) => el().querySelector<HTMLButtonElement>(`#resolve-model-${id}`);

    function showModels(models: UserAiModel[]): void {
      (settingsService.getUserModels as Mock).mockReturnValue(of(models));
      component.loadModels();
      fixture.detectChanges();
    }

    /** Clicks Resolve… on row 5 with the dialog's own open stubbed, then answers the reload with `after`. */
    function resolveFlash(result: ModelResolutionResult, after: UserAiModel[]): void {
      vi.spyOn(component.resolutionDialog!, 'open').mockImplementation(() => {});
      resolveButton(5)!.click();
      (settingsService.getUserModels as Mock).mockReturnValue(of(after));
      component.resolutionDialog!.resolved.emit(result);
      fixture.detectChanges();
    }

    it('flags a retired user row with a badge, a notice and a Resolve… button named for the row', () => {
      showModels([flash(RETIRED), TUNED, gptX(AVAILABLE)]);

      const row = userRow(5);
      const badge = row.querySelector('.status-badge.badge-availability')!;
      expect(text(badge)).toBe('Removed');
      expect(badge.classList).toContain('badge-warning');
      expect(badge.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(badge.getAttribute('interestfor')).toBe('tip-availability-u-5');
      expect(text(el().querySelector('#tip-availability-u-5')))
        .toBe('Flash 3.7 was removed from the model catalog on September 30, 2026.');

      const notice = row.querySelector('app-model-availability-notice .model-availability-notice')!;
      expect(text(notice.querySelector('.man-sentence')))
        .toBe('Flash 3.7 was removed from the model catalog on September 30, 2026.');

      const button = resolveButton(5)!;
      expect(notice.querySelector('.alert-actions')!.contains(button)).toBe(true);
      expect(button.classList).toContain('btn-ghost');
      expect(button.getAttribute('aria-haspopup')).toBe('dialog');
      expect(button.getAttribute('aria-label')).toBe('Resolve "Flash 3.7"');
      expect(text(button)).toBe('Resolve…');
    });

    it('opens the resolution dialog for the row', () => {
      showModels([flash(RETIRED)]);
      const open = vi.spyOn(component.resolutionDialog!, 'open').mockImplementation(() => {});

      resolveButton(5)!.click();

      expect(open).toHaveBeenCalledWith({
        id: 5, provider: 'Google', modelId: 'gemini-3.7-flash', displayName: 'Flash 3.7', availability: RETIRED
      });
    });

    it('reloads after a switch, says so and focuses the next flagged row\'s Resolve…', async () => {
      showModels([flash(RETIRED), gptX(NOT_IN_CATALOG)]);
      const switched = flash(AVAILABLE, {
        modelId: 'gemini-3.8-flash', displayName: 'Gemini 3.8 Flash', displayNameMode: 'model_name', modelCatalogMode: 'catalog'
      });

      resolveFlash({ changes: [], blockers: [], model: switched }, [switched, gptX(NOT_IN_CATALOG)]);
      await fixture.whenStable();

      expect(settingsService.getUserModels).toHaveBeenCalledTimes(3);
      const status = el().querySelector('.models-status')!;
      expect(status.getAttribute('role')).toBe('status');
      expect(text(status)).toBe("'Flash 3.7' now uses Gemini 3.8 Flash.");
      expect(resolveButton(5)).toBeNull();
      expect(document.activeElement).toBe(resolveButton(7));
    });

    it('says a kept model is custom and focuses its name when no other row is flagged', async () => {
      showModels([flash(RETIRED), gptX(AVAILABLE)]);
      const kept = flash(CUSTOM, { modelCatalogMode: 'custom' });

      resolveFlash({ changes: [], blockers: [], model: kept }, [kept, gptX(AVAILABLE)]);
      await fixture.whenStable();

      expect(text(el().querySelector('.models-status'))).toBe("'Flash 3.7' is now a custom model.");
      expect(text(userRow(5).querySelector('.badge-neutral'))).toBe('Custom model');
      expect(document.activeElement).toBe(el().querySelector('#user-model-name-5'));
    });

    it('says a deleted model was deleted and focuses the page heading', async () => {
      showModels([flash(RETIRED), gptX(AVAILABLE)]);

      resolveFlash({ changes: [], blockers: [], model: null, deleted: true }, [gptX(AVAILABLE)]);
      await fixture.whenStable();

      expect(text(el().querySelector('.models-status'))).toBe("'Flash 3.7' was deleted.");
      expect(el().querySelector('#user-model-name-5')).toBeNull();
      expect(document.activeElement).toBe(el().querySelector('#models-heading'));
    });

    it('marks a custom-mode row Custom model with no notice', () => {
      showModels([TUNED]);

      const row = userRow(6);
      const badge = row.querySelector('.status-badge.badge-availability')!;
      expect(badge.classList).toContain('badge-neutral');
      expect(text(badge)).toBe('Custom model');
      expect(badge.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(row.querySelector('.model-availability-notice')).toBeNull();
      expect(resolveButton(6)).toBeNull();
    });

    it('tells a flagged system row an administrator must act, with no Resolve…', () => {
      showModels([OLD_SYSTEM]);

      const row = systemRow();
      expect(text(row.querySelector('.status-badge.badge-availability'))).toBe('Removed');
      const sentence = text(row.querySelector('.model-availability-notice .man-sentence'));
      expect(sentence).toBe(`Old System was removed from the model catalog on September 30, 2026. ${SYSTEM_MODEL_ATTENTION_TEXT}`);
      expect(row.querySelector('.model-resolve-btn')).toBeNull();
      expect(row.querySelector('.alert-actions button')).toBeNull();
    });

    it('passes each title-generation option its availability for the picker chip', () => {
      showModels([flash(RETIRED), OLD_SYSTEM]);

      const options = component.titleModelOptions;
      expect(options.find(option => option.key === 'u_5')!.model.modelAvailability).toEqual(RETIRED);
      expect(options.find(option => option.key === 's_40')!.model.modelAvailability).toEqual(RETIRED);
    });
  });
});
