// Runs every test and hook in a ProxyZone, which fakeAsync() and tick() require.
import 'zone.js/plugins/vitest-patch';

// Jasmine removed spyOn spies after each spec; specs here are written for that.
afterEach(() => {
  vi.restoreAllMocks();
});
