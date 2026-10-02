import { fileURLToPath } from 'node:url';
import { playwright } from '@vitest/browser-playwright';
import { defineConfig } from 'vitest/config';

// Merged by @angular/build:unit-test (runnerConfig) into the configuration it generates.
// The browser is configured here rather than with angular.json's "browsers", whose
// provider would replace this one's launch options.
export default defineConfig({
  resolve: {
    alias: [
      // The test bundle otherwise resolves fflate's Node build, which needs node:module.
      { find: /^fflate$/, replacement: fileURLToPath(new URL('./node_modules/fflate/esm/browser.js', import.meta.url)) }
    ],
    // Added by the builder itself only when angular.json names the browsers.
    conditions: ['browser']
  },
  test: {
    slowTestThreshold: 200,
    browser: {
      enabled: true,
      provider: playwright({
        // colorScheme null keeps prefers-color-scheme unforced, as the builder's provider does.
        contextOptions: { colorScheme: null },
        // Full Chromium in its new headless mode. Under the default headless shell, parallel
        // runs intermittently delay canvas.toBlob callbacks by about 7 s.
        launchOptions: { channel: 'chromium' }
      }),
      headless: true,
      ui: false,
      viewport: { width: 800, height: 600 },
      instances: [{ browser: 'chromium' }]
    }
  }
});
