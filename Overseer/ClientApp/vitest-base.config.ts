import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

// Merged by @angular/build:unit-test (runnerConfig) into the configuration it generates.
export default defineConfig({
  resolve: {
    alias: [
      // The test bundle otherwise resolves fflate's Node build, which needs node:module.
      { find: /^fflate$/, replacement: fileURLToPath(new URL('./node_modules/fflate/esm/browser.js', import.meta.url)) }
    ]
  },
  test: {
    slowTestThreshold: 200
  }
});
