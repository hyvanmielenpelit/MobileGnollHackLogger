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
    },
    {
      id: 'custom-uncatalogued',
      displayName: '',
      description: 'custom-uncatalogued',
      createdAt: 1720000000,
      supportedThinkingLevels: [],
      supportedReasoningModes: [],
      supportedReasoningSummaries: [],
      contextWindowSize: 64000,
      maxInputTokens: 64000,
      maxOutputTokens: 2048
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

  describe('Display name options and preview logic', () => {
    beforeEach(() => {
      component.isAdmin = true;
      component.mode = 'add';
      component.apiKey = 'dummy-key';
      fixture.detectChanges();
    });

    it('should not mutate displayName or customDisplayName on model selection', () => {
      component.fetchModels();
      expect(component.availableModels.length).toBe(3);

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');
      expect(component.customDisplayName).toBe('');

      component.pickerModelSelect = 'claude-3-5-sonnet';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');
      expect(component.customDisplayName).toBe('');
    });

    it('should emit catalog displayName in model_name mode', () => {
      component.fetchModels();
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      component.displayNameMode = 'model_name';

      expect(component.getPreviewDisplayName()).toBe('GPT-4o');

      let savedResult: AiModelFormResult | undefined;
      component.save.subscribe((result) => {
        savedResult = result;
      });

      component.onSave();

      expect(savedResult).toBeDefined();
      expect(savedResult!.displayName).toBe('GPT-4o');
      expect(savedResult!.displayNameMode).toBe('model_name');
    });

    it('should fall back to model id in model_name mode when model is uncatalogued or custom', () => {
      component.fetchModels();
      
      // Uncatalogued model (empty catalog displayName)
      component.pickerModelSelect = 'custom-uncatalogued';
      component.onPickerModelSelect();
      component.displayNameMode = 'model_name';
      expect(component.getPreviewDisplayName()).toBe('custom-uncatalogued');

      // Custom model ID
      component.pickerModelSelect = 'custom';
      component.customModelId = 'my-custom-model-id';
      component.onPickerModelSelect();
      expect(component.getPreviewDisplayName()).toBe('my-custom-model-id');

      let savedResult: AiModelFormResult | undefined;
      component.save.subscribe((result) => {
        savedResult = result;
      });

      component.onSave();
      expect(savedResult).toBeDefined();
      expect(savedResult!.displayName).toBe('my-custom-model-id');
      expect(savedResult!.displayNameMode).toBe('model_name');
    });

    it('should always emit model id in model_id mode even if catalog displayName exists', () => {
      component.fetchModels();
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      component.displayNameMode = 'model_id';

      expect(component.getPreviewDisplayName()).toBe('gpt-4o');

      let savedResult: AiModelFormResult | undefined;
      component.save.subscribe((result) => {
        savedResult = result;
      });

      component.onSave();
      expect(savedResult).toBeDefined();
      expect(savedResult!.displayName).toBe('gpt-4o');
      expect(savedResult!.displayNameMode).toBe('model_id');
    });

    it('should emit custom string in custom mode or fall back to model id when empty', () => {
      component.fetchModels();
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      component.displayNameMode = 'custom';

      // Custom string typed
      component.customDisplayName = 'My Special GPT';
      expect(component.getPreviewDisplayName()).toBe('My Special GPT');

      let savedResult: AiModelFormResult | undefined;
      component.save.subscribe((result) => {
        savedResult = result;
      });

      component.onSave();
      expect(savedResult).toBeDefined();
      expect(savedResult!.displayName).toBe('My Special GPT');
      expect(savedResult!.displayNameMode).toBe('custom');

      // Whitespace / empty custom string falls back to model id
      component.customDisplayName = '   ';
      expect(component.getPreviewDisplayName()).toBe('gpt-4o');
      expect(component.getEffectiveDisplayName()).toBe('gpt-4o');
    });

    it('should preserve customDisplayName across model changes in custom mode', () => {
      component.fetchModels();
      component.displayNameMode = 'custom';
      component.customDisplayName = 'My Preserved Name';

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.customDisplayName).toBe('My Preserved Name');

      component.pickerModelSelect = 'claude-3-5-sonnet';
      component.onPickerModelSelect();
      expect(component.customDisplayName).toBe('My Preserved Name');
    });

    it('should restore custom mode and customDisplayName directly in edit mode without inference when displayNameMode is persisted', () => {
      component.mode = 'edit';
      component.initialData = {
        id: 1,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'My Configured Custom Name',
        displayNameMode: 'custom',
        hasApiKey: true
      };

      component.ngOnInit();

      expect(component.displayNameMode).toBe('custom');
      expect(component.customDisplayName).toBe('My Configured Custom Name');
    });

    it('should perform legacy inference when displayNameMode is absent on initialData', () => {
      // Case A: displayName matches catalog displayName ('GPT-4o') -> 'model_name'
      component.mode = 'edit';
      component.initialData = {
        id: 1,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'GPT-4o',
        hasApiKey: true
      };
      component.ngOnInit();
      expect(component.displayNameMode).toBe('model_name');
      expect(component.customDisplayName).toBe('');

      // Case B: displayName matches modelId ('gpt-4o') -> 'model_id'
      component.initialData = {
        id: 2,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'gpt-4o',
        hasApiKey: true
      };
      component.ngOnInit();
      expect(component.displayNameMode).toBe('model_id');
      expect(component.customDisplayName).toBe('');

      // Case C: displayName is custom ('My Legacy Custom GPT') -> 'custom'
      component.initialData = {
        id: 3,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'My Legacy Custom GPT',
        hasApiKey: true
      };
      component.ngOnInit();
      expect(component.displayNameMode).toBe('custom');
      expect(component.customDisplayName).toBe('My Legacy Custom GPT');
    });

    it('should resolve legacy inference even when getAvailableModels returns empty array', () => {
      (settingsService.getAvailableModels as jasmine.Spy).and.returnValue(of([]));

      component.mode = 'edit';
      component.initialData = {
        id: 4,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'Special Unlisted Model Name',
        hasApiKey: true
      };

      component.ngOnInit();

      expect(component.pickerModelSelect).toBe('custom');
      expect(component.displayNameMode).toBe('custom');
      expect(component.customDisplayName).toBe('Special Unlisted Model Name');
    });

    it('should validate admin display name characters and give descriptive error', () => {
      component.fetchModels();
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      component.displayNameMode = 'custom';
      component.customDisplayName = 'Invalid / Name @ 123!';

      component.onSave();

      expect(component.modelError).toContain('Display Name can only contain letters, numbers, spaces, underscores, dashes, and dots.');
    });
  });

  describe('Non-admin mode', () => {
    beforeEach(() => {
      component.isAdmin = false;
      component.mode = 'add';
      fixture.detectChanges();
    });

    it('should not mutate displayName or customDisplayName on catalog or custom model selection', () => {
      component.fetchModels();
      expect(component.displayName).toBe('');
      expect(component.customDisplayName).toBe('');

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');
      expect(component.customDisplayName).toBe('');

      component.pickerModelSelect = 'custom';
      component.onPickerModelSelect();
      expect(component.displayName).toBe('');
      expect(component.customDisplayName).toBe('');
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
