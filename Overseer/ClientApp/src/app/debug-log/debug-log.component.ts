import { Component, OnInit } from '@angular/core';
import { DebugService, DebugLogEntry } from '../services/debug.service';
import { CommonModule, Location } from '@angular/common';

@Component({
  selector: 'app-debug-log',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './debug-log.component.html',
  styleUrl: './debug-log.component.scss'
})
export class DebugLogComponent implements OnInit {
  logs: DebugLogEntry[] = [];

  constructor(private debugService: DebugService, private location: Location) {}

  ngOnInit(): void {
    this.logs = this.debugService.getLogs();
  }

  goBack(): void {
    this.location.back();
  }

  clearLogs(): void {
    this.logs.length = 0; // Clear the array in-place
  }
}
