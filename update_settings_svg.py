import re

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "r", encoding="utf-8") as f:
    content = f.read()

orig_spans = """              <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." style="color: #ffc107; font-size: 1.5em; transform: translateY(-10px); display: inline-block;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." style="color: #28a745; font-size: 1.5em; transform: translateY(-10px); display: inline-block;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." style="color: #dc3545; font-size: 1.5em; transform: translateY(-10px); display: inline-block;">&#9888;</span>"""

new_svgs = """              <svg *ngIf="apiKey.length > 0" title="API key set but not saved yet." width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#ffc107" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="20 6 9 17 4 12"></polyline>
              </svg>
              <svg *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#28a745" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="20 6 9 17 4 12"></polyline>
              </svg>
              <svg *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#dc3545" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                <line x1="12" y1="9" x2="12" y2="13"></line>
                <line x1="12" y1="17" x2="12.01" y2="17"></line>
              </svg>"""

content = content.replace(orig_spans, new_svgs)

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "w", encoding="utf-8") as f:
    f.write(content)
