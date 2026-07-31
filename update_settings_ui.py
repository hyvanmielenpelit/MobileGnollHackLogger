import re

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "r", encoding="utf-8") as f:
    content = f.read()

# 1. API Key
orig1 = """        <div>
          <label>API Key (Leave blank to keep existing)</label>
          <div style="display: flex; gap: 10px; align-items: center;">
            <input type="password" [(ngModel)]="apiKey" name="apiKey" class="gh-input" style="flex: 1;" />"""
new1 = """        <div>
          <label for="apiKeyInput">API Key</label>
          <span id="apiKeyHint" class="form-hint">Leave blank to keep existing</span>
          <div style="display: flex; gap: 10px; align-items: center; margin-top: 5px;">
            <input type="password" id="apiKeyInput" [(ngModel)]="apiKey" name="apiKey" class="gh-input" style="flex: 1;" aria-describedby="apiKeyHint" />"""
content = content.replace(orig1, new1)

# 2. Spoiler-Free Mode
orig2 = """        <div style="margin-top: 20px; margin-bottom: 20px;">
          <label style="display: flex; align-items: center; gap: 10px; cursor: pointer;">
            <input type="checkbox" [(ngModel)]="spoilerFreeMode" name="spoilerFreeMode" style="width: auto; margin: 0;" />
            <span>Spoiler-Free Mode (Limit hints to avoid spoiling secrets)</span>
          </label>
        </div>"""
new2 = """        <div style="margin-top: 20px; margin-bottom: 20px;">
          <label style="display: flex; align-items: center; gap: 10px; cursor: pointer;">
            <input type="checkbox" [(ngModel)]="spoilerFreeMode" name="spoilerFreeMode" style="width: auto; margin: 0;" aria-describedby="spoilerFreeHint" />
            <span>Spoiler-Free Mode</span>
          </label>
          <span id="spoilerFreeHint" class="form-hint" style="margin-left: 28px; margin-top: 4px;">Limit hints to avoid spoiling secrets</span>
        </div>"""
content = content.replace(orig2, new2)

# 3. Max Tokens
orig3 = """        <div style="display: flex; gap: 10px; margin-bottom: 20px;">
          <div style="flex: 1;">
            <label>Max Input Tokens (Leave blank for no limit)</label>
            <input type="number" [(ngModel)]="maxInputTokens" name="maxInputTokens" class="gh-input" style="width: 100%;" />
          </div>
          <div style="flex: 1;">
            <label>Max Output Tokens (Leave blank for default)</label>
            <input type="number" [(ngModel)]="maxOutputTokens" name="maxOutputTokens" class="gh-input" style="width: 100%;" />
          </div>
        </div>"""
new3 = """        <div style="display: flex; gap: 30px; margin-bottom: 20px;">
          <div style="flex: 1;">
            <label for="maxInputTokensInput">Max Input Tokens</label>
            <span id="maxInputHint" class="form-hint">Leave blank for no limit</span>
            <input type="number" id="maxInputTokensInput" [(ngModel)]="maxInputTokens" name="maxInputTokens" class="gh-input" style="width: 100%; margin-top: 5px;" aria-describedby="maxInputHint" />
          </div>
          <div style="flex: 1;">
            <label for="maxOutputTokensInput">Max Output Tokens</label>
            <span id="maxOutputHint" class="form-hint">Leave blank for default</span>
            <input type="number" id="maxOutputTokensInput" [(ngModel)]="maxOutputTokens" name="maxOutputTokens" class="gh-input" style="width: 100%; margin-top: 5px;" aria-describedby="maxOutputHint" />
          </div>
        </div>"""
content = content.replace(orig3, new3)

# 4. Add form-hint class
orig_styles = """    .model-row { margin-bottom: 15px; }
    label { display: block; margin-bottom: 5px; font-weight: bold; }
    .success { color: #28a745; margin-left: 10px; font-weight: bold; }"""
new_styles = """    .model-row { margin-bottom: 15px; }
    label { display: block; margin-bottom: 5px; font-weight: bold; }
    .form-hint { font-size: 0.85em; color: #aaa; font-weight: normal; display: block; }
    .success { color: #28a745; margin-left: 10px; font-weight: bold; }"""
content = content.replace(orig_styles, new_styles)

# 5. Default Spoiler-Free mode to true
orig_default = "spoilerFreeMode: boolean = false;"
new_default = "spoilerFreeMode: boolean = true;"
content = content.replace(orig_default, new_default)

with open(r"C:\hmp\MobileGnollHackLogger\Overseer\ClientApp\src\app\settings\settings.component.ts", "w", encoding="utf-8") as f:
    f.write(content)
