const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

async function main() {
  const args = process.argv.slice(2);
  if (args.length < 2) {
    console.error('Usage: node generate-release-notes.js <project> <version>');
    process.exit(1);
  }

  const project = args[0].toLowerCase();
  const newVersion = args[1];
  
  let tagPrefix, pathFilters, outFilePath;
  
  if (project === 'overseer') {
    tagPrefix = 'overseer/v';
    pathFilters = ['Overseer/', 'Overseer.Tests/', 'GnollHackServer.Data/'];
    outFilePath = path.join(__dirname, '..', 'Overseer', 'Data', 'release-notes.json');
  } else if (project === 'account') {
    tagPrefix = 'account/v';
    pathFilters = ['MobileGnollHackLogger/', 'GnollHackServer.Data/'];
    outFilePath = path.join(__dirname, '..', 'MobileGnollHackLogger', 'wwwroot', 'release-notes-account.json');
  } else {
    console.error('Invalid project. Must be "overseer" or "account".');
    process.exit(1);
  }

  // 1. Find the latest anchor tag
  let latestTag = '';
  try {
    latestTag = execSync(`git describe --tags --match "${tagPrefix}*" --abbrev=0`, { encoding: 'utf-8' }).trim();
  } catch (err) {
    console.error(`Error: No Git tags found with prefix '${tagPrefix}'.`);
    console.error(`Please create an initial anchor tag first (e.g., git tag ${tagPrefix}1.0.2 && git push origin ${tagPrefix}1.0.2).`);
    process.exit(1);
  }

  console.log(`Latest tag found for ${project}: ${latestTag}`);

  // 2. Get the commits since that tag for the specific paths
  const logCommand = `git log ${latestTag}..HEAD --oneline -- ${pathFilters.join(' ')}`;
  let gitLog = '';
  try {
    gitLog = execSync(logCommand, { encoding: 'utf-8' }).trim();
  } catch (err) {
    console.error('Failed to run git log:', err.message);
    process.exit(1);
  }

  if (!gitLog) {
    console.log(`No commits found for ${project} since ${latestTag}. Exiting.`);
    process.exit(0);
  }

  console.log(`Found ${gitLog.split('\n').length} commits for ${project}. Generating release notes...`);

  // 3. Prepare the AI prompt
  const today = new Date().toISOString().split('T')[0];
  const prompt = `You are a technical writer generating release notes for the "${project}" project.
Here is the git commit log since the last release:

${gitLog}

Based on these commits, write a brief, user-friendly summary of the overall changes, and categorize the key updates into a list of changes.

Classification Rules:
- "feature": Entirely new functionality or capability that did not exist before.
- "improvement": Enhancement to an existing feature (e.g., performance, UX polish, refactoring that is user-visible).
- "fix": A bug correction or defect resolution.
- "security": Security-related fix or hardening.

Exclusion Rules:
Do NOT include version bumps, dependency updates, CI/CD pipeline changes, merge commits, code formatting/linting, or other housekeeping commits that don't affect the end user.

Note: Each item must be independently classified. A single release can have items of different types.

Your output MUST be a valid JSON object following this exact schema:

{
  "version": "${newVersion}",
  "date": "${today}",
  "summary": "A 1-3 sentence summary of the release.",
  "changes": [
    {
      "type": "feature", // MUST be one of: "feature", "fix", "improvement", "security"
      "text": "User friendly description of the change."
    }
  ]
}

DO NOT wrap the JSON in markdown code blocks. Output ONLY the raw JSON object.`;

  // 4. Call the AI
  const provider = process.env.AI_PROVIDER || 'openai';
  const modelName = process.env.AI_MODEL || 'gpt-5.6-sol';
  const fallbackProvider = process.env.FALLBACK_PROVIDER || 'gemini';
  const fallbackModel = process.env.FALLBACK_MODEL || 'gemini-3.1-pro-preview';
  const temperature = parseFloat(process.env.AI_TEMPERATURE || '0.2');
  
  async function callAI(prov, model, temp, text) {
    if (prov === 'gemini') {
      const { GoogleGenAI } = require('@google/genai');
      const ai = new GoogleGenAI({ apiKey: process.env.GEMINI_API_KEY });
      const response = await ai.models.generateContent({
        model: model,
        contents: text,
        config: {
          temperature: temp,
          responseMimeType: 'application/json'
        }
      });
      return response.text;
    } else if (prov === 'openai') {
      const { OpenAI } = require('openai');
      const openai = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });
      const config = {
        model: model,
        messages: [{ role: 'user', content: text }],
        response_format: { type: "json_object" }
      };
      if (model.startsWith('o1') || model.startsWith('o3')) {
         config.reasoning_effort = process.env.AI_REASONING_EFFORT || 'high';
      } else {
         config.temperature = temp;
      }
      const response = await openai.chat.completions.create(config);
      return response.choices[0].message.content;
    } else {
      throw new Error(`Unsupported AI provider: ${prov}`);
    }
  }

  let aiJsonResponse = '';
  try {
    console.log(`Calling primary AI (${provider} : ${modelName})...`);
    aiJsonResponse = await callAI(provider, modelName, temperature, prompt);
  } catch (err) {
    console.warn(`Primary AI (${provider}) failed:`, err.message);
    if (fallbackProvider) {
      console.log(`Calling fallback AI (${fallbackProvider} : ${fallbackModel})...`);
      try {
        aiJsonResponse = await callAI(fallbackProvider, fallbackModel, temperature, prompt);
      } catch (fallbackErr) {
        console.error('Error calling fallback AI API:', fallbackErr.message);
        process.exit(1);
      }
    } else {
      process.exit(1);
    }
  }

  // 5. Parse and save
  let newReleaseNote;
  try {
    // Strip possible markdown blocks if the model ignored instructions
    const cleanJson = aiJsonResponse.replace(/```json/g, '').replace(/```/g, '').trim();
    newReleaseNote = JSON.parse(cleanJson);
  } catch (err) {
    console.error('Failed to parse AI output as JSON:', err.message);
    console.error('Raw output:', aiJsonResponse);
    process.exit(1);
  }
  
  let existingNotes = [];
  if (fs.existsSync(outFilePath)) {
    try {
      const raw = fs.readFileSync(outFilePath, 'utf8');
      existingNotes = JSON.parse(raw);
    } catch(e) {
      console.warn(`Could not parse existing ${outFilePath}. Will create new array.`);
    }
  }
  
  existingNotes.unshift(newReleaseNote);
  
  // Ensure the directory exists
  const dir = path.dirname(outFilePath);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }

  fs.writeFileSync(outFilePath, JSON.stringify(existingNotes, null, 2), 'utf8');
  console.log(`Successfully prepended release notes for v${newVersion} to ${outFilePath}`);
}

main().catch(console.error);
