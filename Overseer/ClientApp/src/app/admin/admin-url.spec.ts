import { UrlSegment, convertToParamMap } from '@angular/router';
import {
  ADMIN_SUBTAB_SLUGS, ADMIN_TAB_SLUGS, AdminTabId, adminCommands, adminUrlMatcher, defaultAdminSubTab,
  isAdminTabId, legacyAdminPath, parseAdminPath
} from './admin-url';
import type { BenchmarkSubTab } from './benchmark/state/benchmark-shell-bridge.service';

const segments = (...paths: string[]) => paths.map(p => new UrlSegment(p, {}));

describe('admin URL scheme', () => {
  describe('adminUrlMatcher', () => {
    it('matches admin with zero, one or two further segments', () => {
      expect(adminUrlMatcher(segments('admin'))?.posParams).toEqual({});
      expect(adminUrlMatcher(segments('admin', 'users'))?.posParams?.['tab'].path).toBe('users');
      const both = adminUrlMatcher(segments('admin', 'users', 'groups'));
      expect(both?.posParams?.['tab'].path).toBe('users');
      expect(both?.posParams?.['subtab'].path).toBe('groups');
      expect(both?.consumed.length).toBe(3);
    });

    it('rejects other first segments, no segments, and four segments', () => {
      expect(adminUrlMatcher(segments())).toBeNull();
      expect(adminUrlMatcher(segments('chat'))).toBeNull();
      expect(adminUrlMatcher(segments('administrator', 'users'))).toBeNull();
      expect(adminUrlMatcher(segments('admin', 'users', 'groups', 'extra'))).toBeNull();
    });
  });

  describe('adminCommands', () => {
    it('omits the default sub-tab', () => {
      expect(adminCommands('users')).toEqual(['/admin', 'users']);
      expect(adminCommands('users', 'users')).toEqual(['/admin', 'users']);
      expect(adminCommands('devtools', 'source')).toEqual(['/admin', 'devtools']);
      expect(adminCommands('benchmark', 'run')).toEqual(['/admin', 'gnollbench']);
      expect(adminCommands('configs', '')).toEqual(['/admin', 'configs']);
    });

    it('adds the slug of any other sub-tab', () => {
      expect(adminCommands('users', 'groups')).toEqual(['/admin', 'users', 'groups']);
      expect(adminCommands('configs', 'api-keys')).toEqual(['/admin', 'configs', 'api-keys']);
      expect(adminCommands('benchmark', 'multirun')).toEqual(['/admin', 'gnollbench', 'multi-run']);
      expect(adminCommands('benchmark', 'modelcomparison')).toEqual(['/admin', 'gnollbench', 'model-comparison']);
      expect(adminCommands('devtools', 'config-health')).toEqual(['/admin', 'devtools', 'config-health']);
    });

    it('ignores an unknown sub-tab and a sub-tab on a tab without sub-tabs', () => {
      expect(adminCommands('users', 'bogus')).toEqual(['/admin', 'users']);
      expect(adminCommands('database', 'groups')).toEqual(['/admin', 'database']);
    });
  });

  describe('parseAdminPath', () => {
    it('reads every tab and sub-tab from its canonical path', () => {
      for (const tab of Object.keys(ADMIN_TAB_SLUGS) as AdminTabId[]) {
        const entries = ADMIN_SUBTAB_SLUGS[tab];
        if (!entries) {
          expect(parseAdminPath(ADMIN_TAB_SLUGS[tab], null)).toEqual(
            { tab, subTab: null, canonical: ['/admin', ADMIN_TAB_SLUGS[tab]], isCanonical: true });
          continue;
        }
        for (const [id, slug] of entries) {
          const parsed = parseAdminPath(ADMIN_TAB_SLUGS[tab], slug === '' ? null : slug);
          expect(parsed.tab).toBe(tab);
          expect(parsed.subTab).toBe(id);
          expect(parsed.isCanonical).toBe(true);
          expect(parsed.canonical).toEqual(adminCommands(tab, id));
        }
      }
    });

    it('normalizes /admin to /admin/users', () => {
      expect(parseAdminPath(null, null)).toEqual(
        { tab: 'users', subTab: 'users', canonical: ['/admin', 'users'], isCanonical: false });
    });

    it('corrects an unknown tab to Users', () => {
      const parsed = parseAdminPath('bogus', 'groups');
      expect(parsed.tab).toBe('users');
      expect(parsed.isCanonical).toBe(false);
    });

    it('corrects an unknown sub-tab to the default', () => {
      expect(parseAdminPath('users', 'bogus')).toEqual(
        { tab: 'users', subTab: 'users', canonical: ['/admin', 'users'], isCanonical: false });
    });

    it('drops a sub-tab segment on a tab without sub-tabs', () => {
      expect(parseAdminPath('database', 'groups')).toEqual(
        { tab: 'database', subTab: null, canonical: ['/admin', 'database'], isCanonical: false });
    });

    it('accepts the default sub-tab by its id and normalizes it away', () => {
      expect(parseAdminPath('users', 'users')).toEqual(
        { tab: 'users', subTab: 'users', canonical: ['/admin', 'users'], isCanonical: false });
      expect(parseAdminPath('devtools', 'source')).toEqual(
        { tab: 'devtools', subTab: 'source', canonical: ['/admin', 'devtools'], isCanonical: false });
    });

    it('does not accept a code id that differs from its slug as a canonical path', () => {
      const parsed = parseAdminPath('gnollbench', 'multirun');
      expect(parsed.subTab).toBe('multirun');
      expect(parsed.canonical).toEqual(['/admin', 'gnollbench', 'multi-run']);
      expect(parsed.isCanonical).toBe(false);
      expect(parseAdminPath('benchmark', null).tab).toBe('users');
    });
  });

  describe('legacyAdminPath', () => {
    const legacy = (params: Record<string, string>) => legacyAdminPath(convertToParamMap(params));

    it('returns null without a tab parameter', () => {
      expect(legacy({})).toBeNull();
      expect(legacy({ subtab: 'deletions', sessionId: 'abc' })).toBeNull();
    });

    it('maps every old tab', () => {
      expect(legacy({ tab: 'users' })).toEqual(['/admin', 'users']);
      expect(legacy({ tab: 'groups' })).toEqual(['/admin', 'users', 'groups']);
      expect(legacy({ tab: 'apikeys' })).toEqual(['/admin', 'configs', 'api-keys']);
      expect(legacy({ tab: 'configs' })).toEqual(['/admin', 'configs']);
      expect(legacy({ tab: 'database' })).toEqual(['/admin', 'database']);
      expect(legacy({ tab: 'telemetry' })).toEqual(['/admin', 'telemetry']);
      expect(legacy({ tab: 'security' })).toEqual(['/admin', 'security']);
      expect(legacy({ tab: 'benchmark' })).toEqual(['/admin', 'gnollbench']);
      expect(legacy({ tab: 'devtools' })).toEqual(['/admin', 'devtools']);
      expect(legacy({ tab: 'bogus' })).toEqual(['/admin', 'users']);
    });

    it('maps the old Users sub-tabs', () => {
      expect(legacy({ tab: 'users', subtab: 'users' })).toEqual(['/admin', 'users']);
      expect(legacy({ tab: 'users', subtab: 'deletions' })).toEqual(['/admin', 'users', 'deletions']);
      expect(legacy({ tab: 'users', subtab: 'log' })).toEqual(['/admin', 'users', 'log']);
      expect(legacy({ tab: 'users', subtab: 'bogus' })).toEqual(['/admin', 'users']);
    });

    it('maps every GnollBench sub-tab by its code id', () => {
      for (const [id, slug] of ADMIN_SUBTAB_SLUGS.benchmark!) {
        expect(legacy({ tab: 'benchmark', subtab: id })).toEqual(slug ? ['/admin', 'gnollbench', slug] : ['/admin', 'gnollbench']);
      }
    });
  });

  it('gives every GnollBench sub-tab a slug, in tab order', () => {
    const order: BenchmarkSubTab[] =
      ['run', 'history', 'multirun', 'multisuite', 'suites', 'profiles', 'modelcomparison', 'chatconsistency'];
    expect(ADMIN_SUBTAB_SLUGS.benchmark!.map(([id]) => id)).toEqual(order);
    expect(ADMIN_SUBTAB_SLUGS.benchmark!.map(([, slug]) => slug)).toEqual(
      ['', 'history', 'multi-run', 'multi-suite', 'suites', 'profiles', 'model-comparison', 'chat-consistency']);
  });

  it('knows the tab ids and defaults', () => {
    expect(isAdminTabId('devtools')).toBe(true);
    expect(isAdminTabId('groups')).toBe(false);
    expect(isAdminTabId(null)).toBe(false);
    expect(defaultAdminSubTab('users')).toBe('users');
    expect(defaultAdminSubTab('benchmark')).toBe('run');
    expect(defaultAdminSubTab('database')).toBeNull();
  });
});
