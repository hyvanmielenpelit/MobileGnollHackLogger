import { Component, EventEmitter, Input, Output, OnInit, OnChanges, SimpleChanges, OnDestroy, ViewChild, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminService, AnalyticsResponse, AnalyticsUserRow } from '../../services/admin.service';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartType } from 'chart.js';
import ChartDataLabels from 'chartjs-plugin-datalabels';
import { Subject, Subscription, of } from 'rxjs';
import { debounceTime, switchMap, catchError, tap } from 'rxjs/operators';

@Component({
  selector: 'app-config-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, BaseChartDirective],
  templateUrl: './config-analytics.component.html',
  styleUrl: './config-analytics.component.scss'
})
export class ConfigAnalyticsComponent implements OnInit, OnChanges, OnDestroy {
  @Input() configId!: number;
  @Input() configName!: string;
  @Output() close = new EventEmitter<void>();

  @ViewChild(BaseChartDirective) chart?: BaseChartDirective;

  // Filter state
  mode: 'all' | 'individual' = 'all';
  dataType: 'requests' | 'tokens' = 'requests';
  timeSpan: '7d' | '30d' | '90d' | '1y' | 'custom' = '30d';
  
  // Custom dates
  customStart: string = '';
  customEnd: string = '';
  
  // Username filter
  usernameFilter: string = '';
  private filterSubject = new Subject<string>();
  private filterSub?: Subscription;
  private loadRequestSubject = new Subject<void>();
  private loadRequestSub?: Subscription;

  // Chart data
  loading = false;
  chartData: ChartConfiguration['data'] = {
    labels: [],
    datasets: []
  };

  chartPlugins = [ChartDataLabels];
  chartType: ChartType = 'bar';

  chartOptions: ChartConfiguration['options'] = {
    indexAxis: 'y', // Horizontal bars
    responsive: true,
    maintainAspectRatio: false,
    animation: { duration: 400 },
    layout: {
      padding: {
        right: 60 // Space for datalabels
      }
    },
    scales: {
      x: { 
        beginAtZero: true, 
        grid: { color: 'rgba(255,255,255,0.05)' } 
      },
      y: { 
        grid: { display: false } 
      }
    },
    plugins: {
      legend: { 
        display: true, 
        position: 'top', 
        labels: { color: '#ccc' } 
      },
      datalabels: {
        clip: false, // Prevent cutting off labels near the edge
        anchor: 'end',
        align: 'end',
        color: '#ccc',
        font: { weight: 'bold', size: 11 },
        formatter: (value) => value.toLocaleString()
      }
    }
  };

  constructor(private adminService: AdminService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.filterSub = this.filterSubject.pipe(
      debounceTime(400)
    ).subscribe(val => {
      this.usernameFilter = val;
      console.log('Username filter changed, triggering load:', val);
      this.triggerLoad();
    });

    this.loadRequestSub = this.loadRequestSubject.pipe(
      debounceTime(100),
      tap(() => {
        console.log('Load request triggered. Setting loading = true');
        this.loading = true;
      }),
      switchMap(() => {
        const req = this.buildRequest();
        console.log('Sending request to getConfigAnalytics:', req);
        return this.adminService.getConfigAnalytics(this.configId, req).pipe(
          catchError(err => {
            console.error('Failed to load analytics', err);
            return of(null);
          })
        );
      })
    ).subscribe(res => {
      this.loading = false;
      if (res) {
        console.log('Received analytics response:', res);
        this.updateChart(res);
      } else {
        console.log('Received empty/null analytics response.');
      }
      this.cdr.detectChanges();
    });

    // Initial load
    console.log('ngOnInit initialized. Triggering initial load.');
    this.triggerLoad();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['configId'] && this.configId && !changes['configId'].isFirstChange()) {
      console.log('configId changed, triggering reload.');
      this.triggerLoad();
    }
  }

  ngOnDestroy() {
    this.filterSub?.unsubscribe();
    this.loadRequestSub?.unsubscribe();
  }

  onFilterChange() {
    console.log('Filter changed manually (mode/dataType/timeSpan)');
    // If switching out of individual, reset username filter implicitly by reloading
    this.triggerLoad();
  }

  onUsernameFilterChange(val: string) {
    this.filterSubject.next(val);
  }

  private triggerLoad() {
    this.loadRequestSubject.next();
  }

  private buildRequest() {
    // Calculate dates
    let startDateStr = '';
    let endDateStr = '';

    if (this.timeSpan === 'custom') {
      startDateStr = this.customStart;
      endDateStr = this.customEnd;
    } else {
      const now = new Date();
      let days = 30;
      switch (this.timeSpan) {
        case '7d': days = 7; break;
        case '30d': days = 30; break;
        case '90d': days = 90; break;
        case '1y': days = 365; break;
      }
      const startDate = new Date(now.getTime() - days * 24 * 60 * 60 * 1000);
      startDateStr = startDate.toISOString().split('T')[0];
      // endDate is implied as today
    }

    return {
      startDate: startDateStr,
      endDate: endDateStr,
      mode: this.mode,
      usernameFilter: this.mode === 'individual' ? this.usernameFilter : ''
    };
  }

  private updateChart(res: AnalyticsResponse) {
    const labels = res.rows.map(r => r.userName || r.userId);
    let datasets: any[] = [];

    if (this.dataType === 'requests') {
      datasets = [
        {
          data: res.rows.map(r => r.chatRequests),
          label: 'Chat Requests',
          backgroundColor: 'rgba(224, 186, 109, 0.85)', // gold
          borderColor: 'rgba(224, 186, 109, 1)',
          borderWidth: 1,
          maxBarThickness: 30
        },
        {
          data: res.rows.map(r => r.titleRequests),
          label: 'Title Requests',
          backgroundColor: 'rgba(100, 181, 246, 0.85)', // blue
          borderColor: 'rgba(100, 181, 246, 1)',
          borderWidth: 1,
          maxBarThickness: 30
        }
      ];
    } else {
      datasets = [
        {
          data: res.rows.map(r => r.inputTokens),
          label: 'Input Tokens',
          backgroundColor: 'rgba(224, 186, 109, 0.85)', // gold
          borderColor: 'rgba(224, 186, 109, 1)',
          borderWidth: 1,
          maxBarThickness: 30
        },
        {
          data: res.rows.map(r => r.outputTokens),
          label: 'Output Tokens',
          backgroundColor: 'rgba(129, 199, 132, 0.85)', // green
          borderColor: 'rgba(129, 199, 132, 1)',
          borderWidth: 1,
          maxBarThickness: 30
        }
      ];
    }

    // Assign new object to trigger ng2-charts OnPush detection
    this.chartData = {
      labels,
      datasets
    };

    if (this.chart) {
      this.chart.update();
    }
  }
}
