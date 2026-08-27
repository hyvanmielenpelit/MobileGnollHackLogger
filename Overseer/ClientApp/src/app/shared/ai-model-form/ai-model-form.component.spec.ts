import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AiModelFormComponent, AiModelFormResult } from './ai-model-form.component';
import { SettingsService, ApiModelDto } from '../../services/settings.service';

describe('AiModelFormComponent', () => {
  let component: AiModelFormComponent;
  let fixture: ComponentFixture<AiModelFormComponent>;
  let settingsService: SettingsService;

  const mockModels: ApiModelDto[] = [
    {
      id: 'gpt-4o',
      displayName: 'GPT-4o',
      description: 'GPT-4o Omnimodel',
      createdAt: 1700000000,
      supportedThinkingLevels: [],
      supportedReasoningModes: [],
      supportedReasoningSummaries: [],
      contextWindowSize: 128000,
      maxInputTokens: 128000,
      maxOutputTokens: 4096
    },
    {
      id: 'claude-3-5-sonnet',
      displayName: 'Claude 3.5 Sonnet',
      description: 'Claude 3.5 Sonnet v2',
      createdAt: 1710000000,
      supportedThinkingLevels: ['low', 'medium', 'high'],
      supportedReasoningModes: [],
      supportedReasoningSummaries: [],
      contextWindowSize: 200000,
      maxInputTokens: 200000,
      maxOutputTokens: 8192
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AiModelFormComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    settingsService = TestBed.inject(SettingsService);
    spyOn(settingsService, 'getAvailableModels').and.returnValue(of(mockModels));

    fixture = TestBed.createComponent(AiModelFormComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Admin mode model selection and display name lifecycle', () => {
    beforeEach(() => {
      component.isAdmin = true;
      component.mode = 'add';
      component.apiKey = 'dummy-key';
      fixture.detectChanges();
    });

    it('should auto-populate displayName when selecting catalog models in admin mode', () => {
      component.fetchModels();
      expect(component.availableModels.length).toBe(2);

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('GPT-4o Omnimodel');
      expect(component.lastAutoDisplayName).toBe('GPT-4o Omnimodel');

      component.pickerModelSelect = 'claude-3-5-sonnet';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('Claude 3.5 Sonnet v2');
      expect(component.lastAutoDisplayName).toBe('Claude 3.5 Sonnet v2');
    });

    it('should reset displayName and lastAutoDisplayName to empty string when selecting Custom after a catalog model', () => {
      component.fetchModels();

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('GPT-4o Omnimodel');

      component.pickerModelSelect = 'custom';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');
      expect(component.lastAutoDisplayName).toBe('');
    });

    it('should repopulate displayName when switching from Custom back to a catalog model', () => {
      component.fetchModels();

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('GPT-4o Omnimodel');

      component.pickerModelSelect = 'custom';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');

      component.pickerModelSelect = 'claude-3-5-sonnet';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('Claude 3.5 Sonnet v2');
      expect(component.lastAutoDisplayName).toBe('Claude 3.5 Sonnet v2');
    });

    it('should preserve manually customized displayName when switching to Custom', () => {
      component.fetchModels();

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('GPT-4o Omnimodel');

      // User manually customizes the display name
      component.displayName = 'My Custom GPT';

      // Switch to custom
      component.pickerModelSelect = 'custom';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('My Custom GPT');
    });

    it('should fall back finalDisplayName to custom model ID onSave when displayName is empty', () => {
      component.fetchModels();

      component.pickerModelSelect = 'custom';
      component.customModelId = 'custom-llama-3';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');

      let savedResult: AiModelFormResult | undefined;
      component.save.subscribe((result) => {
        savedResult = result;
      });

      component.onSave();

      expect(savedResult).toBeDefined();
      expect(savedResult!.modelId).toBe('custom-llama-3');
      expect(savedResult!.displayName).toBe('custom-llama-3');
    });
  });

  describe('Non-admin mode', () => {
    beforeEach(() => {
      component.isAdmin = false;
      component.mode = 'add';
      fixture.detectChanges();
    });

    it('should not mutate displayName on catalog or custom model selection', () => {
      component.fetchModels();
      expect(component.displayName).toBe('');

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');

      component.pickerModelSelect = 'custom';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');
    });
  });

  describe('Edit mode initialization and custom fallback', () => {
    it('should fallback model and thinkingLevel/reasoningMode/reasoningSummary to custom when model is not in availableModels', () => {
      component.isAdmin = true;
      component.mode = 'edit';
      component.initialData = {
        id: 1,
        provider: 'OpenAI',
        modelId: 'deprecated-model-v1',
        thinkingLevel: 'high',
        reasoningMode: 'pro',
        reasoningSummary: 'auto',
        serviceTier: 'custom-tier',
        hasApiKey: true
      };

      component.ngOnInit();

      expect(component.pickerModelSelect).toBe('custom');
      expect(component.customModelId).toBe('deprecated-model-v1');
      expect(component.pickerThinkingLevelSelect).toBe('custom');
      expect(component.customThinkingLevel).toBe('high');
      expect(component.pickerReasoningModeSelect).toBe('custom');
      expect(component.customReasoningMode).toBe('pro');
      expect(component.pickerReasoningSummarySelect).toBe('custom');
      expect(component.customReasoningSummary).toBe('auto');
      expect(component.pickerServiceTierSelect).toBe('custom');
      expect(component.customServiceTier).toBe('custom-tier');
    });

    it('should keep standard thinkingLevel selection when model is in availableModels and level is supported', () => {
      component.isAdmin = true;
      component.mode = 'edit';
      component.initialData = {
        id: 2,
        provider: 'Anthropic',
        modelId: 'claude-3-5-sonnet',
        thinkingLevel: 'medium',
        hasApiKey: true
      };

      component.ngOnInit();

      expect(component.pickerModelSelect).toBe('claude-3-5-sonnet');
      expect(component.pickerThinkingLevelSelect).toBe('medium');
      expect(component.customThinkingLevel).toBe('');
    });

    it('should fallback thinkingLevel to custom when model is in availableModels but level is not supported', () => {
      component.isAdmin = true;
      component.mode = 'edit';
      component.initialData = {
        id: 3,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        thinkingLevel: 'high',
        hasApiKey: true
      };

      component.ngOnInit();

      expect(component.pickerModelSelect).toBe('gpt-4o');
      expect(component.pickerThinkingLevelSelect).toBe('custom');
      expect(component.customThinkingLevel).toBe('high');
    });

    it('should fallback all configured parameters to custom when availableModels is empty', () => {
      (settingsService.getAvailableModels as jasmine.Spy).and.returnValue(of([]));

      component.isAdmin = true;
      component.mode = 'edit';
      component.initialData = {
        id: 4,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        thinkingLevel: 'low',
        hasApiKey: true
      };

      component.ngOnInit();

      expect(component.pickerModelSelect).toBe('custom');
      expect(component.customModelId).toBe('gpt-4o');
      expect(component.pickerThinkingLevelSelect).toBe('custom');
      expect(component.customThinkingLevel).toBe('low');
    });
  });
});
