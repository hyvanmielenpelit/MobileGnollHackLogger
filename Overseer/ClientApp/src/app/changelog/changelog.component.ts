import { Component, OnInit, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ChangelogService } from '../services/changelog.service';
import { ReleaseNote, ReleaseChange } from '../services/release-note.model';
import { RelativeTimePipe } from '../chat/relative-time.pipe';

@Component({
  selector: 'app-changelog',
  standalone: true,
  imports: [CommonModule, RelativeTimePipe],
  templateUrl: './changelog.component.html',
  styleUrl: './changelog.component.scss'
})
export class ChangelogComponent implements OnInit {
  private changelogService = inject(ChangelogService);
  private cdr = inject(ChangeDetectorRef);
  private sanitizer = inject(DomSanitizer);
  
  notes: ReleaseNote[] = [];
  currentPage: number = 0;
  pageSize: number = 10;
  loading: boolean = true;
  error: boolean = false;
  errorMessage: string = '';
  Math = Math;

  get paginatedNotes(): ReleaseNote[] {
    const startIndex = this.currentPage * this.pageSize;
    return this.notes.slice(startIndex, startIndex + this.pageSize);
  }

  ngOnInit(): void {
    this.changelogService.getReleaseNotes().subscribe({
      next: (data) => {
        this.pageSize = data.pageSize || 10;
        this.notes = data.notes;
        this.loading = false;
        if (this.notes && this.notes.length > 0) {
          // Stop animation once the user opens the page for the newest version
          this.changelogService.markAsSeen(this.notes[0].version);
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Failed to load release notes', err);
        this.error = true;
        this.loading = false;
        this.errorMessage = err.message || JSON.stringify(err);
        this.cdr.detectChanges();
      }
    });
  }

  previousPage(): void {
    if (this.currentPage > 0) {
      this.currentPage--;
    }
  }

  nextPage(): void {
    if ((this.currentPage + 1) * this.pageSize < this.notes.length) {
      this.currentPage++;
    }
  }

  getGroupedChanges(note: ReleaseNote): { type: string; label: string; items: ReleaseChange[] }[] {
    const featureItems = note.changes?.filter(c => c.type === 'feature') || [];
    const improvementItems = note.changes?.filter(c => c.type === 'improvement') || [];
    const fixItems = note.changes?.filter(c => c.type === 'fix') || [];
    const securityItems = note.changes?.filter(c => c.type === 'security') || [];
    
    const groups = [];
    if (featureItems.length > 0) groups.push({ type: 'feature', label: 'Features', items: featureItems });
    if (improvementItems.length > 0) groups.push({ type: 'improvement', label: 'Improvements', items: improvementItems });
    if (fixItems.length > 0) groups.push({ type: 'fix', label: 'Bug Fixes', items: fixItems });
    if (securityItems.length > 0) groups.push({ type: 'security', label: 'Security', items: securityItems });
    return groups;
  }

  getIconSvgForType(type: string): SafeHtml {
    let svgPath = '';
    switch(type) {
      case 'feature': 
        svgPath = '<path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/>';
        break;
      case 'fix': 
        // Modern wrench icon (maintenance/fix)
        svgPath = '<path d="M22.7 19l-9.1-9.1c.9-2.3.4-5-1.5-6.9-2-2-5-2.4-7.4-1.3L9 6 6 9 1.6 4.7C.4 7.1.9 10.1 2.9 12.1c1.9 1.9 4.6 2.4 6.9 1.5l9.1 9.1c.4.4 1 .4 1.4 0l2.3-2.3c.5-.4.5-1.1.1-1.4z"/>';
        break;
      case 'improvement': 
        svgPath = '<path d="M13.17 4L18 8.83V20H6V4h7.17zM14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm-2 12c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm4 3.43c0-.81-.48-1.53-1.22-1.85-.85-.37-1.79-.58-2.78-.58s-1.93.21-2.78.58C8.48 15.9 8 16.62 8 17.43V18h8v-.57z"/>';
        break;
      case 'security': 
        svgPath = '<path d="M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z"/>';
        break;
      default: 
        svgPath = '<path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/>';
        break;
    }
    return this.sanitizer.bypassSecurityTrustHtml(svgPath);
  }
}
