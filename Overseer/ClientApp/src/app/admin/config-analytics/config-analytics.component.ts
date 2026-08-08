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
  
  // Chart sizing and gap configuration (easily configurable exact pixel values)
  readonly BAR_THICKNESS = 20; // Thickness of each individual bar
  readonly BAR_GAP_PX = 4;     // Gap between bars of the same user (e.g. Chat vs Title requests)
  readonly USER_GAP_PX = 8;   // Gap between different users
  
  chartHeight = 250; // Dynamically calculated below
  
  // Custom dates
  customStart: string = '';
  customEnd: string = '';
  
  // Pagination
  page: number = 1;
  pageSize: number = 10;
  totalCount: number = 0;
  pageSizes: number[] = [10, 25, 50, 100];
  
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
        grid: { color: 'rgba(255,255,255,0.05)' },
        ticks: { color: '#b0b0b0', font: { family: "'Inter', 'Segoe UI', Roboto, sans-serif" } }
      },
      y: { 
        grid: { display: false },
        ticks: { 
          color: '#ffffff', 
          font: { size: 14, weight: 'bold', family: "'Inter', 'Segoe UI', Roboto, sans-serif" } 
        }
      }
    },
    plugins: {
      legend: { 
        display: true, 
        position: 'top', 
        labels: { color: '#ffffff', font: { size: 13, family: "'Inter', 'Segoe UI', Roboto, sans-serif" } } 
      },
      datalabels: {
        clip: false, // Prevent cutting off labels near the edge
        anchor: 'end',
        align: 'end',
        color: '#ffffff',
        font: { weight: 'bold', size: 12, family: "'Inter', 'Segoe UI', Roboto, sans-serif" },
        formatter: (value) => value.toLocaleString()
      },
      tooltip: {
        backgroundColor: 'rgba(22, 22, 22, 0.95)', // Opaque dark background
        titleColor: '#ffffff',
        bodyColor: '#e0e0e0',
        borderColor: 'rgba(224, 186, 109, 0.4)', // Clear gold border
        borderWidth: 1,
        padding: 10,
        cornerRadius: 6,
        displayColors: true,
        boxPadding: 5, // space between color box and text
        titleFont: { family: "'Inter', 'Segoe UI', Roboto, sans-serif", size: 14, weight: 'bold' },
        bodyFont: { family: "'Inter', 'Segoe UI', Roboto, sans-serif", size: 13 }
      }
    }
  };

  constructor(private adminService: AdminService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    this.filterSub = this.filterSubject.pipe(
      debounceTime(400)
    ).subscribe(val => {
      this.usernameFilter = val;
      this.page = 1; // Reset to page 1 on filter
      this.triggerLoad();
    });

    this.loadRequestSub = this.loadRequestSubject.pipe(
      debounceTime(100),
      tap(() => this.loading = true),
      switchMap(() => {
        const req = this.buildRequest();
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
        this.totalCount = res.totalCount || 0;
        this.updateChart(res);
      }
      this.cdr.detectChanges();
    });

    // Initial load
    this.triggerLoad();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['configId'] && this.configId && !changes['configId'].isFirstChange()) {
      this.triggerLoad();
    }
  }

  ngOnDestroy() {
    this.filterSub?.unsubscribe();
    this.loadRequestSub?.unsubscribe();
  }

  onFilterChange() {
    this.page = 1;
    this.triggerLoad();
  }

  onUsernameFilterChange(val: string) {
    this.filterSubject.next(val);
  }
  
  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  get pageNumbers(): (number | string)[] {
    const total = this.totalPages;
    const current = this.page;
    const sibling = 1;

    // If few enough pages, show them all (up to 7 pages fits in our 7 slots)
    if (total <= 7) {
      return Array.from({ length: total }, (_, i) => i + 1);
    }

    const left = Math.max(current - sibling, 1);
    const right = Math.min(current + sibling, total);
    const showLeftDots = left > 2;
    const showRightDots = right < total - 1;

    if (!showLeftDots && showRightDots) {
      const count = 3 + 2 * sibling;
      return [...Array.from({ length: count }, (_, i) => i + 1), '…', total];
    }
    if (showLeftDots && !showRightDots) {
      const count = 3 + 2 * sibling;
      return [1, '…', ...Array.from({ length: count }, (_, i) => total - count + 1 + i)];
    }
    // Both ellipses
    const mid = Array.from({ length: right - left + 1 }, (_, i) => left + i);
    return [1, '…', ...mid, '…', total];
  }

  onPageNumberClick(p: number | string) {
    if (typeof p === 'number') {
      this.onPageChange(p);
    }
  }

  trackByPage(index: number): number {
    return index;
  }

  onPageChange(newPage: number) {
    if (newPage >= 1 && newPage <= this.totalPages && newPage !== this.page) {
      this.page = newPage;
      this.triggerLoad();
    }
  }

  onPageSizeChange() {
    this.page = 1;
    this.triggerLoad();
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
      usernameFilter: this.mode === 'individual' ? this.usernameFilter : '',
      page: this.page,
      pageSize: this.pageSize
    };
  }

  private updateChart(res: AnalyticsResponse) {
    const labels = res.rows.map(r => r.userName || r.userId);
    const numUsers = labels.length || 1;
    
    // Calculate required height mathematically to guarantee exact pixel sizes in Chart.js
    const heightPerUser = (2 * this.BAR_THICKNESS) + this.BAR_GAP_PX + this.USER_GAP_PX;
    this.chartHeight = (numUsers * heightPerUser) + 70; // +70px for axes and padding
    
    // Chart.js requires percentage configurations. We can perfectly reverse-engineer them 
    // from our pixel requirements to force the exact gaps we want.
    const barSpace = (2 * this.BAR_THICKNESS) + this.BAR_GAP_PX;
    const calcCategoryPercentage = barSpace / heightPerUser;
    const calcBarPercentage = (2 * this.BAR_THICKNESS) / barSpace;

    let datasets: any[] = [];

    if (this.dataType === 'requests') {
      datasets = [
        {
          data: res.rows.map(r => r.chatRequests),
          label: 'Chat Requests',
          backgroundColor: 'rgba(224, 186, 109, 0.85)', // gold
          borderColor: 'rgba(224, 186, 109, 1)',
          borderWidth: 1,
          borderRadius: 4,
          barPercentage: calcBarPercentage,
          categoryPercentage: calcCategoryPercentage
        },
        {
          data: res.rows.map(r => r.titleRequests),
          label: 'Title Requests',
          backgroundColor: 'rgba(100, 181, 246, 0.85)', // blue
          borderColor: 'rgba(100, 181, 246, 1)',
          borderWidth: 1,
          borderRadius: 4,
          barPercentage: calcBarPercentage,
          categoryPercentage: calcCategoryPercentage
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
          borderRadius: 4,
          barPercentage: calcBarPercentage,
          categoryPercentage: calcCategoryPercentage
        },
        {
          data: res.rows.map(r => r.outputTokens),
          label: 'Output Tokens',
          backgroundColor: 'rgba(129, 199, 132, 0.85)', // green
          borderColor: 'rgba(129, 199, 132, 1)',
          borderWidth: 1,
          borderRadius: 4,
          barPercentage: calcBarPercentage,
          categoryPercentage: calcCategoryPercentage
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
