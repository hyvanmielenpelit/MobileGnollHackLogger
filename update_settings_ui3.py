import re

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "r", encoding="utf-8") as f:
    content = f.read()

orig_api_key = """          <div style="display: grid; grid-template-columns: 1fr 190px; gap: 10px; align-items: center; margin-top: 5px;">
            <input type="password" id="apiKeyInput" [(ngModel)]="apiKey" name="apiKey" class="gh-input" aria-describedby="apiKeyHint" />
            <div style="display: flex; justify-content: flex-end; align-items: center; gap: 10px; height: 100%;">
              <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." style="color: #ffc107; font-size: 1.5em; flex-shrink: 0;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." style="color: #28a745; font-size: 1.5em; flex-shrink: 0;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." style="color: #dc3545; font-size: 1.5em; flex-shrink: 0;">&#9888;</span>
              <button *ngIf="hasApiKey" type="button" class="btn-gh btn-gh-delete" style="width: 140px; min-width: 140px; min-height: 36px; padding: 5px 10px;" (click)="deleteApiKey()" [disabled]="loading">Delete</button>
            </div>
          </div>"""

new_api_key = """          <div style="display: grid; grid-template-columns: 1fr 40px 140px; gap: 10px; align-items: center; margin-top: 5px;">
            <input type="password" id="apiKeyInput" [(ngModel)]="apiKey" name="apiKey" class="gh-input" aria-describedby="apiKeyHint" style="margin: 0;" />
            <div style="display: flex; justify-content: center; align-items: center;">
              <span *ngIf="apiKey.length > 0" title="API key set but not saved yet." style="color: #ffc107; font-size: 1.5em; line-height: 1;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && hasApiKey" title="An API key is currently saved." style="color: #28a745; font-size: 1.5em; line-height: 1;">&#10004;</span>
              <span *ngIf="apiKey.length === 0 && !hasApiKey" title="No API key saved." style="color: #dc3545; font-size: 1.5em; line-height: 1;">&#9888;</span>
            </div>
            <button *ngIf="hasApiKey" type="button" class="btn-gh btn-gh-delete" style="width: 140px; min-width: 140px; min-height: 36px; padding: 5px 10px; margin: 0;" (click)="deleteApiKey()" [disabled]="loading">Delete</button>
          </div>"""

content = content.replace(orig_api_key, new_api_key)

orig_model = """          <div style="display: grid; grid-template-columns: 1fr 190px; gap: 10px; align-items: center;">
            <input type="text" [(ngModel)]="model" name="model" class="gh-input" />
            <div style="display: flex; justify-content: flex-end; align-items: center; gap: 10px; height: 100%;">
              <button type="button" class="btn-gh" style="width: 190px;" (click)="checkModels()" [disabled]="loadingModels">
                {{ loadingModels ? 'Checking...' : 'Check Models' }}
              </button>
            </div>
          </div>"""

new_model = """          <div style="display: grid; grid-template-columns: 1fr 40px 140px; gap: 10px; align-items: center;">
            <input type="text" [(ngModel)]="model" name="model" class="gh-input" style="margin: 0;" />
            <button type="button" class="btn-gh" style="grid-column: 2 / span 2; width: 100%; margin: 0;" (click)="checkModels()" [disabled]="loadingModels">
              {{ loadingModels ? 'Checking...' : 'Check Models' }}
            </button>
          </div>"""

content = content.replace(orig_model, new_model)

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "w", encoding="utf-8") as f:
    f.write(content)
