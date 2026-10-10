import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AiModelFormComponent, AiModelFormResult } from './ai-model-form.component';
import { SettingsService, ApiModelDto } from '../../services/settings.service';
import { AdminService, EndpointPolicySummaryDto } from '../../services/admin.service';
import { ModelAvailability } from '../model-availability/model-availability';

describe('AiModelFormComponent', () => {
  let component: AiModelFormComponent;
  let fixture: ComponentFixture<AiModelFormComponent>;
  let settingsService: SettingsService;
  let adminService: AdminService;

  /** The permissive default, so an existing test's endpoint fields stay enabled. */
  const openPolicy: EndpointPolicySummaryDto = {
    customEndpointsEnabled: true,
    allowedHostPatterns: ['gateway.example.com', '*.openai.azure.com'],
    allowedHeaderNames: ['X-Gateway-Tenant'],
    allowLoopback: false
  };

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
      maxOutputTokens: 4096,
      defaultPricing: {
        inputPerMillion: 5.0,
        outputPerMillion: 15.0,
        cachedInputPerMillion: 2.5,
        asOf: '2024-05-13'
      }
    },
    {
      id: 'claude-3-5-sonnet',
      displayName: 'Claude 3.5 Sonnet',
      description: 'Claude 3.5 Sonnet v2',
      createdAt: 1710000000,
      supportedThinkingLevels: ['low', 'medium', 'high'],
      defaultThinkingLevel: 'high',
      supportedReasoningModes: [],
      supportedReasoningSummaries: [],
      contextWindowSize: 200000,
      maxInputTokens: 200000,
      maxOutputTokens: 8192
    },
    {
      id: 'claude-3-haiku',
      displayName: 'Claude 3 Haiku',
      description: 'Claude 3 Haiku',
      createdAt: 1715000000,
      supportedThinkingLevels: ['low', 'medium'],
      recommendedThinkingLevel: 'low',
      supportedReasoningModes: [],
      supportedReasoningSummaries: [],
      contextWindowSize: 100000,
      maxInputTokens: 100000,
      maxOutputTokens: 4096
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
    vi.spyOn(settingsService, 'getAvailableModels').mockReturnValue(of(mockModels));

    adminService = TestBed.inject(AdminService);
    vi.spyOn(adminService, 'getEndpointPolicy').mockReturnValue(of(openPolicy));

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
      expect(component.availableModels.length).toBe(4);

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
      (settingsService.getAvailableModels as Mock).mockReturnValue(of([]));

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
      (settingsService.getAvailableModels as Mock).mockReturnValue(of([]));

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

  describe('Edit mode property preservation on model switch', () => {
    beforeEach(() => {
      component.isAdmin = true;
      component.mode = 'edit';
      component.initialData = {
        id: 10,
        provider: 'Anthropic',
        modelId: 'claude-3-5-sonnet',
        thinkingLevel: 'medium',
        reasoningMode: '',
        reasoningSummary: '',
        serviceTier: 'auto',
        maxInputTokens: 50000,
        maxOutputTokens: 2000,
        hasApiKey: true
      };
      component.ngOnInit();
      fixture.detectChanges();
    });

    it('should preserve valid thinkingLevel, serviceTier, and tokens when switching models', () => {
      expect(component.pickerModelSelect).toBe('claude-3-5-sonnet');
      expect(component.thinkingLevel).toBe('medium');
      expect(component.serviceTier).toBe('auto');
      expect(component.maxInputTokens).toBe(50000);
      expect(component.maxOutputTokens).toBe(2000);

      // Switch to claude-3-haiku which supports 'medium' and has 100k input / 4096 output limits
      component.pickerModelSelect = 'claude-3-haiku';
      component.onPickerModelSelect();

      expect(component.modelId).toBe('claude-3-haiku');
      expect(component.thinkingLevel).toBe('medium');
      expect(component.pickerThinkingLevelSelect).toBe('medium');
      expect(component.customThinkingLevel).toBe('');
      expect(component.serviceTier).toBe('auto');
      expect(component.maxInputTokens).toBe(50000);
      expect(component.maxOutputTokens).toBe(2000);
    });

    it('should gracefully fall back invalid thinkingLevel to recommended/medium when switching models', () => {
      // Set initial data with 'high' thinking level
      component.initialData = {
        id: 11,
        provider: 'Anthropic',
        modelId: 'claude-3-5-sonnet',
        thinkingLevel: 'high',
        maxInputTokens: 50000,
        maxOutputTokens: 2000,
        hasApiKey: true
      };
      component.ngOnInit();

      expect(component.thinkingLevel).toBe('high');

      // Switch to claude-3-haiku which only supports ['low', 'medium'] with recommended 'low'
      component.pickerModelSelect = 'claude-3-haiku';
      component.onPickerModelSelect();

      expect(component.thinkingLevel).toBe('low');
      expect(component.pickerThinkingLevelSelect).toBe('low');
      expect(component.maxInputTokens).toBe(50000);
    });

    it('should reset thinkingLevel to empty when switching to a model that does not support thinking', () => {
      expect(component.thinkingLevel).toBe('medium');

      // Switch to gpt-4o which does not support thinking
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();

      expect(component.thinkingLevel).toBe('');
      expect(component.pickerThinkingLevelSelect).toBe('');
      expect(component.customThinkingLevel).toBe('');
    });

    it('should clamp maxInputTokens and maxOutputTokens if they exceed the new model limits', () => {
      component.initialData = {
        id: 12,
        provider: 'Anthropic',
        modelId: 'claude-3-5-sonnet',
        thinkingLevel: 'medium',
        maxInputTokens: 150000,
        maxOutputTokens: 6000,
        hasApiKey: true
      };
      component.ngOnInit();

      // Switch to custom-uncatalogued which has maxInput: 64000, maxOutput: 2048
      component.pickerModelSelect = 'custom-uncatalogued';
      component.onPickerModelSelect();

      expect(component.maxInputTokens).toBe(64000);
      expect(component.maxOutputTokens).toBe(2048);
    });

    it('should preserve properties into custom fields when switching to Custom model', () => {
      expect(component.thinkingLevel).toBe('medium');
      expect(component.serviceTier).toBe('auto');

      component.pickerModelSelect = 'custom';
      component.onPickerModelSelect();

      expect(component.modelId).toBe('');
      expect(component.pickerThinkingLevelSelect).toBe('custom');
      expect(component.customThinkingLevel).toBe('medium');
      expect(component.pickerServiceTierSelect).toBe('custom');
      expect(component.customServiceTier).toBe('auto');
      expect(component.maxInputTokens).toBe(50000);
      expect(component.maxOutputTokens).toBe(2000);
    });

    it('should ignore onProviderChange when in Edit mode', () => {
      expect(component.thinkingLevel).toBe('medium');
      expect(component.maxInputTokens).toBe(50000);
      expect(component.availableModels.length).toBe(4);

      // Attempt provider change in Edit mode
      component.provider = 'OpenAI';
      component.onProviderChange();

      // Properties and available models must not be purged or reset
      expect(component.thinkingLevel).toBe('medium');
      expect(component.maxInputTokens).toBe(50000);
      expect(component.availableModels.length).toBe(4);
    });

    it('should disable the provider select in Edit mode and enable it in Add mode', async () => {
      const editFixture = TestBed.createComponent(AiModelFormComponent);
      const editComponent = editFixture.componentInstance;
      editComponent.mode = 'edit';
      editComponent.providers = ['Anthropic', 'OpenAI'];
      editFixture.detectChanges();
      await editFixture.whenStable();
      editFixture.detectChanges();

      const editSelect: HTMLSelectElement = editFixture.nativeElement.querySelector('select');
      expect(editSelect.disabled).toBe(true);
      expect(editSelect.title).toBe('Provider cannot be changed for an existing configuration');

      const addFixture = TestBed.createComponent(AiModelFormComponent);
      const addComponent = addFixture.componentInstance;
      addComponent.mode = 'add';
      addComponent.providers = ['Anthropic', 'OpenAI'];
      addFixture.detectChanges();
      await addFixture.whenStable();
      addFixture.detectChanges();

      const addSelect: HTMLSelectElement = addFixture.nativeElement.querySelector('select');
      expect(addSelect.disabled).toBe(false);
      expect(addSelect.title).toBe('');
    });

    it('should apply fresh defaults on model pick in Add mode', () => {
      const addFixture = TestBed.createComponent(AiModelFormComponent);
      const addComponent = addFixture.componentInstance;
      addComponent.isAdmin = true;
      addComponent.mode = 'add';
      addComponent.apiKey = 'dummy-key';
      addComponent.ngOnInit();
      addComponent.fetchModels();
      addFixture.detectChanges();

      addComponent.pickerModelSelect = 'claude-3-haiku';
      addComponent.onPickerModelSelect();

      expect(addComponent.thinkingLevel).toBe('low');
      expect(addComponent.pickerThinkingLevelSelect).toBe('low');
      expect(addComponent.maxInputTokens).toBe(100000);
      expect(addComponent.maxOutputTokens).toBe(4096);
    });

    it('should compute defaultThinkingLevelLabel correctly based on selected model defaultThinkingLevel', () => {
      component.fetchModels();
      component.pickerModelSelect = 'claude-3-5-sonnet';
      component.onPickerModelSelect();
      expect(component.defaultThinkingLevelLabel).toBe('Default (Adaptive, High)');

      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      expect(component.defaultThinkingLevelLabel).toBe('Default');

      component.pickerModelSelect = 'custom';
      component.onPickerModelSelect();
      expect(component.defaultThinkingLevelLabel).toBe('Default');
    });
  });

  describe('Pricing logic', () => {
    it('should render default price when model with pricing is selected', () => {
      component.isAdmin = false;
      fixture.detectChanges();
      component.fetchModels();
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();
      fixture.detectChanges();

      const fieldsets = fixture.nativeElement.querySelectorAll('fieldset.pricing-fieldset');
      expect(fieldsets.length).toBe(1);
      const legend = fieldsets[0].querySelector('legend');
      expect(legend?.textContent?.trim()).toBe('Pricing');

      const html = fixture.nativeElement.innerHTML;
      expect(html).toContain('Pricing Model');
      expect(html).toContain('Input:');
      expect(html).toContain('$5.00');
      expect(html).toContain('Output:');
      expect(html).toContain('$15.00');
      expect(html).toContain('Cached Input:');
      expect(html).toContain('$2.50');
      expect(html).toContain('/ 1M tokens');
      expect(html).toContain('Catalog pricing as of 2024-05-13 (USD)');
    });

    it('should render pricing block for admin too', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();
      component.fetchModels();
      component.pickerModelSelect = 'claude-3-5-sonnet'; // no pricing
      component.onPickerModelSelect();
      fixture.detectChanges();

      const fieldsets = fixture.nativeElement.querySelectorAll('fieldset.pricing-fieldset');
      expect(fieldsets.length).toBe(1);
      const legend = fieldsets[0].querySelector('legend');
      expect(legend?.textContent?.trim()).toBe('Pricing');

      const html = fixture.nativeElement.innerHTML;
      expect(html).toContain('Pricing Model');
      expect(html).toContain('No price published in the model catalog');
    });

    it('should seed custom inputs from default pricing when switching to custom', () => {
      component.isAdmin = false;
      fixture.detectChanges();
      component.fetchModels();
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();

      expect(component.pricingMode).toBe('default');
      expect(component.inputPricePerMillion).toBeNull();
      
      component.pricingMode = 'custom';
      component.onPricingModeChange();

      expect(component.inputPricePerMillion).toBe(5.0);
      expect(component.outputPricePerMillion).toBe(15.0);
      expect(component.cachedInputPricePerMillion).toBe(2.5);
    });
  });

  describe('Advanced section', () => {
    const advanced = (): HTMLDetailsElement =>
      fixture.nativeElement.querySelector('details.advanced-section');

    it('should be collapsed and hold the Pricing fieldset in non-admin add mode', () => {
      component.isAdmin = false;
      fixture.detectChanges();

      const details = advanced();
      expect(details).toBeTruthy();
      expect(details.open).toBe(false);
      expect(component.advancedOpen).toBe(false);
      expect(details.querySelector('summary')?.textContent?.trim()).toBe('Advanced');
      expect(details.querySelector('fieldset.pricing-fieldset')).toBeTruthy();
    });

    it('should hold Pricing, Tool Calling, Provider Agreement and Custom Endpoint in admin mode, but not the status checkboxes', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      const details = advanced();
      const legends = Array.from(details.querySelectorAll('legend')).map(l => l.textContent?.trim());
      expect(legends).toEqual(['Pricing', 'Tool Calling', 'Provider Agreement', 'Custom Endpoint']);

      const parallel = details.querySelector('#parallelExecutionModeSelect');
      expect(parallel).toBeTruthy();
      expect(parallel!.closest('fieldset')?.querySelector('legend')?.textContent?.trim())
        .toBe('Tool Calling');
      expect(parallel!.getAttribute('aria-describedby')).toBe('parallelExecutionModeHint');
      expect(details.querySelector('#parallelExecutionModeHint')?.classList).toContain('form-hint');

      const trustFieldset = Array.from(details.querySelectorAll('fieldset'))
        .find(f => f.querySelector('legend')?.textContent?.trim() === 'Provider Agreement')!;
      expect(parallel!.compareDocumentPosition(trustFieldset) & Node.DOCUMENT_POSITION_FOLLOWING)
        .toBeTruthy();

      // Enabled / System Wide and Model Role stay outside Advanced.
      expect(details.querySelector('.checkbox-row')).toBeNull();
      expect(fixture.nativeElement.querySelector('.checkbox-row')).toBeTruthy();
    });

    it('should not render the admin-only advanced blocks for a non-admin', () => {
      component.isAdmin = false;
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('#parallelExecutionModeSelect')).toBeNull();
      const legends = Array.from(fixture.nativeElement.querySelectorAll('legend'))
        .map((l: any) => l.textContent?.trim());
      expect(legends).not.toContain('Provider Agreement');
      expect(legends).not.toContain('Custom Endpoint');
    });

    it('should open when an advanced value is non-default', () => {
      component.mode = 'edit';
      component.isAdmin = true;
      component.initialData = {
        id: 1,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'GPT-4o',
        displayNameMode: 'model_name',
        hasApiKey: false,
        pricingMode: 'default',
        parallelExecutionMode: 2,
        baseUrl: 'https://gw.example'
      };
      fixture.detectChanges();

      expect(component.advancedOpen).toBe(true);
      expect(advanced().open).toBe(true);
    });

    it('should open for a non-admin whose pricing is custom', () => {
      component.mode = 'edit';
      component.isAdmin = false;
      component.initialData = {
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'GPT-4o',
        displayNameMode: 'model_name',
        pricingMode: 'custom'
      };
      fixture.detectChanges();

      expect(component.advancedOpen).toBe(true);
      expect(advanced().open).toBe(true);
    });

    it('should stay closed when every advanced value is at its default', () => {
      component.mode = 'edit';
      component.isAdmin = true;
      component.initialData = {
        id: 1,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'GPT-4o',
        displayNameMode: 'model_name',
        hasApiKey: false,
        pricingMode: 'default',
        parallelExecutionMode: 2
      };
      fixture.detectChanges();

      expect(component.advancedOpen).toBe(false);
      expect(advanced().open).toBe(false);
    });

    it('should keep the section open after the user toggles it', () => {
      component.mode = 'edit';
      component.isAdmin = true;
      component.initialData = {
        id: 1,
        provider: 'OpenAI',
        modelId: 'gpt-4o',
        displayName: 'GPT-4o',
        displayNameMode: 'model_name',
        hasApiKey: false,
        pricingMode: 'default',
        parallelExecutionMode: 2
      };
      fixture.detectChanges();
      expect(component.advancedOpen).toBe(false);

      const details = advanced();
      details.open = true;
      details.dispatchEvent(new Event('toggle'));

      expect(component.advancedOpen).toBe(true);

      fixture.detectChanges();
      expect(advanced().open).toBe(true);
    });

    it('should render Custom Headers as a textarea on a row of its own', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      const headers = fixture.nativeElement.querySelector('#customHeadersInput') as HTMLTextAreaElement;
      expect(headers).toBeTruthy();
      expect(headers.tagName).toBe('TEXTAREA');
      expect(headers.rows).toBe(1);
      expect(headers.maxLength).toBe(4096);

      const group = headers.closest('.form-group')!;
      expect(group.querySelector('#apiVersionInput')).toBeNull();
      expect(fixture.nativeElement.querySelector('#apiVersionInput')).toBeTruthy();
    });

    it('should round-trip the Custom Headers value into the saved result', async () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();
      component.fetchModels();
      component.pickerModelSelect = 'gpt-4o';
      component.onPickerModelSelect();

      component.customHeadersJson = '{"X-A":"1"}';
      fixture.detectChanges();
      await fixture.whenStable();

      const headers = fixture.nativeElement.querySelector('#customHeadersInput') as HTMLTextAreaElement;
      expect(headers.value).toBe('{"X-A":"1"}');

      let savedResult: AiModelFormResult | undefined;
      component.save.subscribe((result) => {
        savedResult = result;
      });
      component.onSave();

      expect(savedResult).toBeDefined();
      expect(savedResult!.customHeadersJson).toBe('{"X-A":"1"}');
    });

    it('should check the key against the endpoint typed into the form, not the public API', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      component.baseUrl = ' https://gateway.example.com/openai ';
      component.customHeadersJson = '{"X-Gateway-Tenant":"acme"}';
      component.apiVersion = '2026-05-01';
      (settingsService.getAvailableModels as Mock).mockClear();

      component.onCheckModels();

      const args = vi.mocked((settingsService.getAvailableModels as Mock)).mock.lastCall!;
      expect(args[3]).toEqual({
        baseUrl: 'https://gateway.example.com/openai',
        customHeadersJson: '{"X-Gateway-Tenant":"acme"}',
        apiVersion: '2026-05-01'
      });
    });

    it('should not treat a verification date with no posture as verified', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      component.postureVerifiedUtc = '2026-09-14';
      component.confidentialityPosture = '';
      fixture.detectChanges();

      expect(component.isPostureVerified).toBe(false);
      expect(component.hasEmptyVerifiedPosture).toBe(true);

      const badge = fixture.nativeElement.querySelector('#postureSelect')!
        .closest('fieldset')!.querySelector('.status-badge') as HTMLElement;
      expect(badge.classList).not.toContain('badge-success');
      expect(badge.textContent!.trim()).toBe('Recorded, unverified: Not established');

      component.confidentialityPosture = 'ZeroRetention';
      fixture.detectChanges();

      expect(component.isPostureVerified).toBe(true);
      expect(badge.classList).toContain('badge-success');
      expect(badge.textContent!.trim()).toBe('Verified 2026-09-14: Zero data retention');
    });

    it('should render the selected posture hint under the select', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      const select = fixture.nativeElement.querySelector('#postureSelect') as HTMLSelectElement;
      expect(select.getAttribute('aria-describedby')).toBe('postureHint');

      const hint = () => fixture.nativeElement.querySelector('#postureHint')!.textContent!.trim();
      expect(hint()).toBe('Nothing has been checked. The honest default.');

      component.confidentialityPosture = 'NoTraining';
      fixture.detectChanges();

      expect(hint())
        .toBe('The provider has undertaken not to train on content. It may still retain it.');
    });

    it('should disable the endpoint fields and say so when no host is allowlisted', async () => {
      (adminService.getEndpointPolicy as Mock).mockReturnValue(of({
        customEndpointsEnabled: false,
        allowedHostPatterns: [],
        allowedHeaderNames: [],
        allowLoopback: false
      } as EndpointPolicySummaryDto));

      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      expect(component.customEndpointsEnabled).toBe(false);

      // ngModel applies a disabled binding on the microtask after the render.
      await fixture.whenStable();
      fixture.detectChanges();

      for (const id of ['#baseUrlInput', '#apiVersionInput', '#customHeadersInput']) {
        const input = fixture.nativeElement.querySelector(id) as HTMLInputElement;
        expect(input).toBeTruthy();
        expect(input.disabled, id).toBe(true);
      }

      const fieldset = fixture.nativeElement.querySelector('.endpoint-fieldset') as HTMLElement;
      expect(fieldset.textContent).toContain('Custom endpoints are switched off on this server');
    });

    it('should list the allowed hosts and header names when custom endpoints are enabled', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      const fieldset = fixture.nativeElement.querySelector('.endpoint-fieldset') as HTMLElement;
      expect(fieldset.textContent).toContain('gateway.example.com, *.openai.azure.com');
      expect(fieldset.textContent).toContain('X-Gateway-Tenant');
      expect((fixture.nativeElement.querySelector('#baseUrlInput') as HTMLInputElement).disabled)
        .toBe(false);
    });

    it('should show a server refusal inside the dialog', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      component.serverError = "The host 'gw.example.com' is not in PrivacySettings:CustomEndpoints:AllowedHostPatterns.";
      fixture.detectChanges();

      const errors = Array.from(fixture.nativeElement.querySelectorAll('.error-message'))
        .map((e: any) => e.textContent as string);
      expect(errors.some(t => t.includes('AllowedHostPatterns'))).toBe(true);
      expect(errors.some(t => t.includes('The configuration was not saved'))).toBe(true);
    });
  });

  describe('Layout areas', () => {
    const el = () => fixture.nativeElement as HTMLElement;
    const layout = () => el().querySelector('.model-form-layout') as HTMLElement;
    const areaClasses = () => Array.from(layout().children)
      .map(c => ['mf-connection', 'mf-model', 'mf-settings', 'mf-advanced'].find(a => c.classList.contains(a)));
    const legendOf = (fieldset: Element | null) =>
      fieldset?.querySelector(':scope > legend')?.textContent?.trim();

    it('orders the four areas Connection, Model, Settings, Advanced for an admin', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      expect(layout().classList).toContain('mf-admin');
      expect(areaClasses()).toEqual(['mf-connection', 'mf-model', 'mf-settings', 'mf-advanced']);
      expect(legendOf(layout().querySelector('.mf-connection'))).toBe('Connection');
      expect(legendOf(layout().querySelector('.mf-model'))).toBe('Model');
      expect(layout().querySelector('.mf-advanced')?.tagName).toBe('DETAILS');
    });

    it('puts the Note, status and Model Role in the admin Configuration group', () => {
      component.isAdmin = true;
      component.apiKey = 'dummy';
      fixture.detectChanges();

      const note = el().querySelector('#configNoteInput') as HTMLInputElement;
      expect(note).toBeTruthy();
      const configuration = note.closest('fieldset.mf-configuration')!;
      expect(legendOf(configuration)).toBe('Configuration');
      expect(configuration.closest('.mf-settings')).toBeTruthy();
      expect(el().querySelector('label[for="configNoteInput"]')?.textContent?.trim()).toBe('Note');

      const role = configuration.querySelector('fieldset.model-role-choice');
      expect(role).toBeTruthy();
      expect(legendOf(role)).toBe('Model Role');
      expect(role!.querySelectorAll('.checkbox-row input[type="checkbox"]').length).toBe(3);
      expect(configuration.textContent).toContain('Enabled');
      expect(configuration.textContent).toContain('System Wide');
    });

    it('keeps the same area order without mf-admin or a Configuration group for a non-admin', () => {
      component.isAdmin = false;
      fixture.detectChanges();

      expect(layout().classList).not.toContain('mf-admin');
      expect(areaClasses()).toEqual(['mf-connection', 'mf-model', 'mf-settings', 'mf-advanced']);
      const legends = Array.from(el().querySelectorAll('legend')).map(l => l.textContent?.trim());
      expect(legends).not.toContain('Configuration');
      expect(legends).not.toContain('Model Role');
      expect(el().querySelector('#configNoteInput')).toBeNull();
      expect(el().querySelector('.mf-settings .model-properties-fieldset')).toBeTruthy();
    });

    it('associates the Provider and Models labels with their selects', () => {
      component.isAdmin = false;
      fixture.detectChanges();

      for (const id of ['providerSelect', 'pickerModelSelect']) {
        const label = el().querySelector(`label[for="${id}"]`) as HTMLLabelElement;
        expect(label, id).toBeTruthy();
        expect(el().querySelector(`#${id}`)?.tagName, id).toBe('SELECT');
      }
      expect(el().querySelector('label[for="providerSelect"]')?.textContent?.trim()).toBe('Provider');
      expect(el().querySelector('label[for="pickerModelSelect"]')?.textContent?.trim()).toBe('Models');
      expect(el().querySelector('#pickerModelSelect')?.classList).toContain('picker-model-select');
    });
  });

  describe('Default / Custom API key choice', () => {
    const el = () => fixture.nativeElement as HTMLElement;
    const defaultRadio = () => el().querySelector('#apiKeyChoiceDefault') as HTMLInputElement;
    const customRadio = () => el().querySelector('#apiKeyChoiceCustom') as HTMLInputElement;
    const reason = () => el().querySelector('#apiKeyChoiceDefaultReason') as HTMLElement | null;

    beforeEach(() => {
      component.isAdmin = true;
      component.providers = ['Anthropic', 'Google', 'OpenAI'];
      component.initialProvider = 'Anthropic';
      component.defaultKeys = { Anthropic: { hasKey: true, keyHint: 'ab12', verified: true } };
    });

    it('preselects Default for a new configuration when the provider has a default key', () => {
      component.mode = 'add';
      fixture.detectChanges();

      expect(component.apiKeyChoice).toBe('default');
      expect(defaultRadio().checked).toBe(true);
      expect(defaultRadio().disabled).toBe(false);
      expect(defaultRadio().closest('label')!.textContent).toContain('The Anthropic default key, …ab12');
      expect(customRadio().closest('label')!.textContent).toContain('A key for this configuration only');
      expect(el().querySelector('.custom-api-key-input')).toBeNull();
      expect(reason()).toBeNull();
    });

    it('names each radio by its short name and describes it by its detail line', () => {
      component.mode = 'add';
      fixture.detectChanges();

      const nameId = defaultRadio().getAttribute('aria-labelledby');
      expect(nameId).toBe('apiKeyChoiceDefaultName');
      expect(el().querySelector('#' + nameId)!.textContent!.trim()).toBe('Default key');
      expect(defaultRadio().getAttribute('aria-describedby')).toBe('apiKeyChoiceDefaultDetail');
      expect(customRadio().getAttribute('aria-labelledby')).toBe('apiKeyChoiceCustomName');
      expect(customRadio().getAttribute('aria-describedby')).toBe('apiKeyChoiceCustomDetail');

      const label = defaultRadio().closest('label') as HTMLElement;
      const name = label.querySelector('.api-key-option-name') as HTMLElement;
      const detail = label.querySelector('.api-key-option-detail') as HTMLElement;
      expect(name).not.toBeNull();
      expect(detail).not.toBeNull();
      expect(name.contains(detail)).toBe(false);
      expect(getComputedStyle(label).display).toBe('grid');
    });

    it('describes the Default radio by both its key line and its reason while a Base URL is filled', () => {
      component.mode = 'add';
      fixture.detectChanges();

      component.onBaseUrlChange('https://gateway.example.com/anthropic');
      fixture.detectChanges();

      expect(defaultRadio().getAttribute('aria-describedby')).toBe('apiKeyChoiceDefaultDetail apiKeyChoiceDefaultReason');
      expect(defaultRadio().closest('label')!.contains(reason())).toBe(true);
    });

    it('keeps a default key that is not verified selectable, and says so', () => {
      component.defaultKeys = { Anthropic: { hasKey: true, keyHint: 'ab12', verified: false } };
      component.mode = 'add';
      fixture.detectChanges();

      expect(defaultRadio().disabled).toBe(false);
      expect(defaultRadio().checked).toBe(true);
      expect(defaultRadio().closest('label')!.textContent).toContain('…ab12 (not verified)');
    });

    it('moves to Custom and disables Default with its reason on a provider without a default key', () => {
      component.mode = 'add';
      fixture.detectChanges();

      component.provider = 'Google';
      component.onProviderChange();
      fixture.detectChanges();

      expect(component.apiKeyChoice).toBe('custom');
      expect(customRadio().checked).toBe(true);
      expect(defaultRadio().disabled).toBe(true);
      expect(defaultRadio().getAttribute('aria-describedby')).toBe('apiKeyChoiceDefaultReason');
      expect(reason()!.textContent!.trim()).toBe('No default Google key. Add one in Admin → API Keys.');
    });

    it('disables Default while a Base URL is filled, and offers it again once it is cleared', () => {
      component.mode = 'add';
      fixture.detectChanges();

      component.onBaseUrlChange('https://gateway.example.com/anthropic');
      fixture.detectChanges();
      expect(component.apiKeyChoice).toBe('custom');
      expect(defaultRadio().disabled).toBe(true);
      expect(reason()!.textContent!.trim()).toBe('A default key works only with the provider\'s own endpoint.');

      component.onBaseUrlChange('');
      fixture.detectChanges();
      expect(component.apiKeyChoice).toBe('default');
      expect(defaultRadio().disabled).toBe(false);
    });

    it('checks models with the default key and saves without sending a key', async () => {
      component.mode = 'add';
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const picker = el().querySelector('.picker-model-select') as HTMLSelectElement;
      expect(picker.disabled).toBe(false);

      (settingsService.getAvailableModels as Mock).mockClear();
      (el().querySelector('.check-models-btn') as HTMLButtonElement).click();
      fixture.detectChanges();

      const args = vi.mocked((settingsService.getAvailableModels as Mock)).mock.lastCall!;
      expect(args[0]).toBe('Anthropic');
      expect(args[1]).toBe('');
      expect(args[4]).toBe(true);

      let saved: AiModelFormResult | undefined;
      component.save.subscribe(result => saved = result);
      component.onSave();

      expect(saved).toBeDefined();
      expect(saved!.useDefaultApiKey).toBe(true);
      expect(saved!.apiKey).toBeUndefined();
    });

    it('sends useDefaultApiKey false with Custom', () => {
      component.mode = 'add';
      // The admin page always passes the new configuration's defaults as initialData.
      component.initialData = { provider: 'Anthropic', isEnabled: true, modelRole: 3 };
      fixture.detectChanges();
      expect(component.apiKeyChoice).toBe('default');

      customRadio().click();
      fixture.detectChanges();

      expect(component.apiKeyChoice).toBe('custom');
      expect(el().querySelector('.custom-api-key-input')).not.toBeNull();

      component.apiKey = 'test-key-not-real-0001';
      component.fetchModels();
      let saved: AiModelFormResult | undefined;
      component.save.subscribe(result => saved = result);
      component.onSave();

      expect(saved!.useDefaultApiKey).toBe(false);
      expect(saved!.apiKey).toBe('test-key-not-real-0001');
    });

    it('opens an existing Default configuration on Default, and needs a key before saving it as Custom', () => {
      component.mode = 'edit';
      component.initialData = {
        id: 9, provider: 'Anthropic', modelId: 'claude-3-5-sonnet', displayName: 'claude-3-5-sonnet',
        displayNameMode: 'model_id', hasApiKey: true, useDefaultApiKey: true, modelRole: 3
      };
      fixture.detectChanges();

      expect(defaultRadio().checked).toBe(true);

      customRadio().click();
      fixture.detectChanges();
      expect(el().querySelector('.custom-api-key-input')).not.toBeNull();

      let emitted = 0;
      component.save.subscribe(() => emitted++);
      component.onSave();
      fixture.detectChanges();

      expect(emitted).toBe(0);
      const error = el().querySelector('#customApiKeyError') as HTMLElement;
      expect(error.textContent!.trim()).toBe('Enter a key for this configuration, or choose Default key.');
      expect((el().querySelector('.custom-api-key-input') as HTMLInputElement).getAttribute('aria-describedby'))
        .toBe('customApiKeyError');
    });

    it('opens a Default configuration on Custom when its default key is gone', () => {
      component.defaultKeys = {};
      component.mode = 'edit';
      component.initialData = {
        id: 10, provider: 'Anthropic', modelId: 'claude-3-5-sonnet', hasApiKey: false, useDefaultApiKey: true
      };
      fixture.detectChanges();

      expect(component.apiKeyChoice).toBe('custom');
      expect(defaultRadio().disabled).toBe(true);
      expect(el().querySelector('.custom-api-key-input')).not.toBeNull();
    });

    it('does not show the key choice outside admin mode', () => {
      component.isAdmin = false;
      fixture.detectChanges();

      expect(defaultRadio()).toBeNull();
      expect(customRadio()).toBeNull();
    });
  });

  describe('Catalog availability notice', () => {
    const RETIRED: ModelAvailability = { status: 'retired', needsAttention: true, retiredOn: '2026-09-30' };
    const el = () => fixture.nativeElement as HTMLElement;
    const notice = () => el().querySelector('.mf-model app-model-availability-notice .model-availability-notice');
    const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();

    beforeEach(() => {
      component.isAdmin = false;
      component.initialData = { id: 1, provider: 'OpenAI', modelId: 'gpt-old', displayName: 'GPT Old' };
    });

    it('shows the notice in the Model area when an edited model is retired', () => {
      component.mode = 'edit';
      component.availability = RETIRED;
      fixture.detectChanges();

      expect(notice()).not.toBeNull();
      expect(text(notice()!.querySelector('.man-sentence'))).toBe(
        'GPT Old was removed from the model catalog on September 30, 2026. '
        + 'Pick a model in the list above to switch, or save as is to keep it flagged.');
    });

    it('shows no notice without an availability that needs attention', () => {
      component.mode = 'edit';
      component.availability = { status: 'available', needsAttention: false };
      fixture.detectChanges();

      expect(notice()).toBeNull();
    });

    it('shows no notice when no availability is passed', () => {
      component.mode = 'edit';
      fixture.detectChanges();

      expect(notice()).toBeNull();
    });

    it('shows no notice in add mode', () => {
      component.mode = 'add';
      component.initialData = undefined;
      component.availability = RETIRED;
      fixture.detectChanges();

      expect(notice()).toBeNull();
    });
  });
});
