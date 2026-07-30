import re
import os

file_path = r'c:\hmp\MobileGnollHackLogger\Overseer\Services\ChatService.cs'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace IAsyncEnumerable<string> with IAsyncEnumerable<ChatEvent>
content = content.replace('IAsyncEnumerable<string>', 'IAsyncEnumerable<ChatEvent>')

# Replace simple error yield returns
content = content.replace('yield return "Error: API Key not configured. Please configure it in Settings.";', 
                          'yield return new ChatEvent { Type = "error", Data = "Error: API Key not configured. Please configure it in Settings." };')
content = content.replace('yield return "Error: Session not found.";', 
                          'yield return new ChatEvent { Type = "error", Data = "Error: Session not found." };')
content = content.replace('yield return $"Provider {provider} is not fully implemented yet in this demo.";', 
                          'yield return new ChatEvent { Type = "error", Data = $"Provider {provider} is not fully implemented yet in this demo." };')

# We need a generic way to wrap the HTTP calls in retries.
# Let's write a replacement block for the OpenAI part.

def replace_provider(content, provider, http_call, success_check, stream_handling, url_desc):
    # This is complex to do with pure regex. Let's just create a refactoring helper script.
    pass

with open(r'c:\hmp\MobileGnollHackLogger\Overseer\Services\refactor.py', 'w') as f:
    f.write('print("Use multi_replace_file_content")')
