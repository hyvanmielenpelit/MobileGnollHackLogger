import re

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "r", encoding="utf-8") as f:
    content = f.read()

orig_spans = """              <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." style="color: #ffc107; font-size: 1.5em;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." style="color: #28a745; font-size: 1.5em;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." style="color: #dc3545; font-size: 1.5em;">&#9888;</span>"""

new_spans = """              <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." style="color: #ffc107; font-size: 1.5em; transform: translateY(2px); display: inline-block;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." style="color: #28a745; font-size: 1.5em; transform: translateY(2px); display: inline-block;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." style="color: #dc3545; font-size: 1.5em; transform: translateY(2px); display: inline-block;">&#9888;</span>"""

content = content.replace(orig_spans, new_spans)

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "w", encoding="utf-8") as f:
    f.write(content)
