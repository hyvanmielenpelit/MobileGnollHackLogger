import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: jasmine.SpyObj<AdminBenchmarkService>;

  beforeEach(async () => {
    benchmarkServiceMock = jasmine.createSpyObj('AdminBenchmarkService', [
      'getSuites',
      'getRuns',
      'getQuestions',
      'getScoringProfiles',
      'startRun',
      'getRun',
      'cancelRun',
      'deleteRun',
      'createSuite',
      'updateSuite',
      'deleteSuite',
      'duplicateSuite',
      'importDefaultSuite'
    ]);

    benchmarkServiceMock.getSuites.and.returnValue(of([
      { id: 1, name: 'Default Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 15 }
    ]));
    benchmarkServiceMock.getScoringProfiles.and.returnValue(of([
      {
        id: 1,
        name: 'Default Intelligence Profile',
        isDefault: true,
        weightAccuracy: 0.55,
        weightCompleteness: 0.25,
        weightConciseness: 0.10,
        weightReadability: 0.10,
        levelScoresJson: '[1, 15, 35, 55, 72, 87, 100]',
        criticalErrorCeiling: 25,
        speedTargetMs: 5000,
        speedDecayK: 25.0,
        maxParallelQuestions: 1,
        createdAtUtc: '2026-09-01T00:00:00Z',
        modifiedAtUtc: '2026-09-01T00:00:00Z'
      }
    ]));
    benchmarkServiceMock.getRuns.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [AdminBenchmarkComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: benchmarkServiceMock },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminBenchmarkComponent);
    component = fixture.componentInstance;
    component.systemConfigs = [
      {
        id: 1,
        displayName: 'Test Model',
        displayNameMode: null,
        provider: 'Anthropic',
        modelId: 'claude-3-5-sonnet',
        thinkingLevel: null,
        reasoningMode: null,
        reasoningSummary: null,
        serviceTier: null,
        maxInputTokens: null,
        maxOutputTokens: null,
        orderIndex: 0,
        isEnabled: true,
        hasApiKey: true,
        isSystemWide: false,
        maxDailyChatRequests: null,
        maxMonthlyChatRequests: null,
        maxTotalChatRequests: null,
        dailyChatRequestsCount: 0,
        monthlyChatRequestsCount: 0,
        totalChatRequestsCount: 0,
        maxDailyTitleRequests: null,
        maxMonthlyTitleRequests: null,
        maxTotalTitleRequests: null,
        dailyTitleRequestsCount: 0,
        monthlyTitleRequestsCount: 0,
        totalTitleRequestsCount: 0,
        maxDailyChatTokens: null,
        maxMonthlyChatTokens: null,
        maxTotalChatTokens: null,
        dailyChatTokensCount: 0,
        monthlyChatTokensCount: 0,
        totalChatTokensCount: 0,
        maxDailyTitleTokens: null,
        maxMonthlyTitleTokens: null,
        maxTotalTitleTokens: null,
        dailyTitleTokensCount: 0,
        monthlyTitleTokensCount: 0,
        totalTitleTokensCount: 0,
        modelRole: 7,
        parallelExecutionMode: 2,
        apiKey: '',
        note: null
      }
    ];
    fixture.detectChanges();
  });

  it('should create and load suites', () => {
    expect(component).toBeTruthy();
    expect(component.suites.length).toBe(1);
    expect(component.suites[0].name).toBe('Default Suite');
  });

  it('should filter benchmarkCapableConfigs based on modelRole bitmask 4', () => {
    expect(component.benchmarkCapableConfigs.length).toBe(1);
    
    // Add non-benchmark model (modelRole = 3: Chat + Title)
    component.systemConfigs.push({
      id: 2,
      displayName: 'Chat Only Model',
      displayNameMode: null,
      provider: 'OpenAI',
      modelId: 'gpt-5.6-luna',
      thinkingLevel: null,
      reasoningMode: null,
      reasoningSummary: null,
      serviceTier: null,
      maxInputTokens: null,
      maxOutputTokens: null,
      orderIndex: 1,
      isEnabled: true,
      hasApiKey: true,
      isSystemWide: false,
      maxDailyChatRequests: null,
      maxMonthlyChatRequests: null,
      maxTotalChatRequests: null,
      dailyChatRequestsCount: 0,
      monthlyChatRequestsCount: 0,
      totalChatRequestsCount: 0,
      maxDailyTitleRequests: null,
      maxMonthlyTitleRequests: null,
      maxTotalTitleRequests: null,
      dailyTitleRequestsCount: 0,
      monthlyTitleRequestsCount: 0,
      totalTitleRequestsCount: 0,
      maxDailyChatTokens: null,
      maxMonthlyChatTokens: null,
      maxTotalChatTokens: null,
      dailyChatTokensCount: 0,
      monthlyChatTokensCount: 0,
      totalChatTokensCount: 0,
      maxDailyTitleTokens: null,
      maxMonthlyTitleTokens: null,
      maxTotalTitleTokens: null,
      dailyTitleTokensCount: 0,
      monthlyTitleTokensCount: 0,
      totalTitleTokensCount: 0,
      modelRole: 3,
      parallelExecutionMode: 0,
      apiKey: '',
      note: null
    });

    expect(component.benchmarkCapableConfigs.length).toBe(1);
    expect(component.benchmarkCapableConfigs[0].id).toBe(1);
  });

  it('should format status strings correctly', () => {
    expect(component.formatStatus(1)).toBe('Running');
    expect(component.formatStatus(2)).toBe('Completed');
    expect(component.formatStatus(3)).toBe('CompletedWithErrors');
    expect(component.formatStatus(4)).toBe('Failed');
    expect(component.formatStatus(5)).toBe('Canceled');
  });

  it('should compute score badge classes correctly', () => {
    expect(component.getScoreBadgeClass(90)).toBe('badge-score-high');
    expect(component.getScoreBadgeClass(65)).toBe('badge-score-mid');
    expect(component.getScoreBadgeClass(40)).toBe('badge-score-low');
    expect(component.getScoreBadgeClass(null)).toBe('badge-score-na');
  });
});
