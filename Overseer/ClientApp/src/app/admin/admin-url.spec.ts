import { UrlSegment, convertToParamMap } from '@angular/router';
import {
  ADMIN_SUBTAB_SLUGS, ADMIN_TAB_SLUGS, AdminTabId, FEEDBACK_FILTER_QUERY_PARAMS, RETIRED_ADMIN_SUBTABS, RETIRED_ADMIN_TAB_SLUGS, adminCommands,
  adminUrlMatcher, clearedFeedbackQueryParams, defaultAdminSubTab,
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
      expect(adminCommands('devtools', 'source')).toEqual(['/admin', 'services']);
      expect(adminCommands('benchmark', 'run')).toEqual(['/admin', 'gnollbench']);
      expect(adminCommands('configs', '')).toEqual(['/admin', 'configs']);
    });

    it('adds the slug of any other sub-tab', () => {
      expect(adminCommands('users', 'groups')).toEqual(['/admin', 'users', 'groups']);
      expect(adminCommands('configs', 'api-keys')).toEqual(['/admin', 'configs', 'api-keys']);
      expect(adminCommands('benchmark', 'multirun')).toEqual(['/admin', 'gnollbench', 'multi-run']);
      expect(adminCommands('benchmark', 'modelcomparison')).toEqual(['/admin', 'gnollbench', 'model-comparison']);
      expect(adminCommands('devtools', 'config-health')).toEqual(['/admin', 'services', 'config-health']);
      expect(adminCommands('devtools', 'sentry')).toEqual(['/admin', 'services', 'developer-tools']);
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
        { tab: 'devtools', subTab: 'source', canonical: ['/admin', 'services'], isCanonical: false });
    });

    it('accepts a sub-tab by its code id and replaces it with the slug', () => {
      for (const tabSlug of ['services', 'devtools']) {
        expect(parseAdminPath(tabSlug, 'sentry')).toEqual(
          { tab: 'devtools', subTab: 'sentry', canonical: ['/admin', 'services', 'developer-tools'], isCanonical: false });
      }
    });

    it('opens a retired tab slug as its tab and replaces it with the current slug', () => {
      expect(RETIRED_ADMIN_TAB_SLUGS['devtools']).toBe('devtools');
      expect(parseAdminPath('devtools', null)).toEqual(
        { tab: 'devtools', subTab: 'source', canonical: ['/admin', 'services'], isCanonical: false });
      expect(parseAdminPath('devtools', 'config-health')).toEqual(
        { tab: 'devtools', subTab: 'config-health', canonical: ['/admin', 'services', 'config-health'], isCanonical: false });
    });

    it('does not read an inherited property name as a retired slug', () => {
      expect(parseAdminPath('constructor', null).tab).toBe('users');
      expect(parseAdminPath('toString', null).tab).toBe('users');
    });

    it('reads the AI Telemetry sub-tabs from their paths, Summary by default', () => {
      expect(parseAdminPath('telemetry', null)).toEqual(
        { tab: 'telemetry', subTab: 'summary', canonical: ['/admin', 'telemetry'], isCanonical: true });
      expect(parseAdminPath('telemetry', 'models')).toEqual(
        { tab: 'telemetry', subTab: 'models', canonical: ['/admin', 'telemetry', 'models'], isCanonical: true });
      expect(parseAdminPath('telemetry', 'users')).toEqual(
        { tab: 'telemetry', subTab: 'users', canonical: ['/admin', 'telemetry', 'users'], isCanonical: true });
      expect(parseAdminPath('telemetry', 'groups')).toEqual(
        { tab: 'telemetry', subTab: 'groups', canonical: ['/admin', 'telemetry', 'groups'], isCanonical: true });
      expect(parseAdminPath('telemetry', 'bogus')).toEqual(
        { tab: 'telemetry', subTab: 'summary', canonical: ['/admin', 'telemetry'], isCanonical: false });
      expect(adminCommands('telemetry', 'summary')).toEqual(['/admin', 'telemetry']);
      expect(adminCommands('telemetry', 'users')).toEqual(['/admin', 'telemetry', 'users']);
      expect(adminCommands('telemetry', 'groups')).toEqual(['/admin', 'telemetry', 'groups']);
    });

    it('has four AI Telemetry sub-tabs and Feedback as a tab of its own at /admin/feedback', () => {
      expect(ADMIN_SUBTAB_SLUGS.telemetry!.map(([id]) => id)).toEqual(['summary', 'models', 'users', 'groups']);
      expect(adminCommands('feedback')).toEqual(['/admin', 'feedback']);
      expect(parseAdminPath('feedback', null)).toEqual(
        { tab: 'feedback', subTab: 'overseer', canonical: ['/admin', 'feedback'], isCanonical: true });
    });

    it('reads the Feedback sub-tabs from their paths, Overseer by default', () => {
      expect(ADMIN_SUBTAB_SLUGS.feedback).toEqual([['overseer', ''], ['gnollbench', 'gnollbench']]);
      expect(defaultAdminSubTab('feedback')).toBe('overseer');
      expect(adminCommands('feedback', 'overseer')).toEqual(['/admin', 'feedback']);
      expect(adminCommands('feedback', 'gnollbench')).toEqual(['/admin', 'feedback', 'gnollbench']);
      expect(parseAdminPath('feedback', 'gnollbench')).toEqual(
        { tab: 'feedback', subTab: 'gnollbench', canonical: ['/admin', 'feedback', 'gnollbench'], isCanonical: true });
      expect(parseAdminPath('feedback', 'overseer')).toEqual(
        { tab: 'feedback', subTab: 'overseer', canonical: ['/admin', 'feedback'], isCanonical: false });
      expect(parseAdminPath('feedback', 'bogus')).toEqual(
        { tab: 'feedback', subTab: 'overseer', canonical: ['/admin', 'feedback'], isCanonical: false });
    });

    it('reads the Security sub-tabs from their paths, Summary by default', () => {
      expect(ADMIN_SUBTAB_SLUGS.security).toEqual([['summary', ''], ['events', 'events'], ['settings', 'settings']]);
      expect(defaultAdminSubTab('security')).toBe('summary');
      expect(adminCommands('security', 'summary')).toEqual(['/admin', 'security']);
      expect(adminCommands('security', 'events')).toEqual(['/admin', 'security', 'events']);
      expect(parseAdminPath('security', null)).toEqual(
        { tab: 'security', subTab: 'summary', canonical: ['/admin', 'security'], isCanonical: true });
      expect(parseAdminPath('security', 'settings')).toEqual(
        { tab: 'security', subTab: 'settings', canonical: ['/admin', 'security', 'settings'], isCanonical: true });
      expect(parseAdminPath('security', 'bogus')).toEqual(
        { tab: 'security', subTab: 'summary', canonical: ['/admin', 'security'], isCanonical: false });
    });

    it('opens the retired /admin/telemetry/feedback path as the Feedback tab\'s Overseer sub-tab and replaces it', () => {
      expect(RETIRED_ADMIN_SUBTABS.telemetry).toEqual({ feedback: 'feedback' });
      expect(parseAdminPath('telemetry', 'feedback')).toEqual(
        { tab: 'feedback', subTab: 'overseer', canonical: ['/admin', 'feedback'], isCanonical: false });
    });

    it('does not retire the GnollBench sub-tab slug', () => {
      expect(Object.hasOwn(RETIRED_ADMIN_TAB_SLUGS, 'gnollbench')).toBe(false);
      expect(Object.values(RETIRED_ADMIN_SUBTABS).some(retired => retired && Object.hasOwn(retired, 'gnollbench'))).toBe(false);
    });

    it('names every Feedback filter parameter, so a sub-tab switch can remove them', () => {
      for (const name of ['status', 'category', 'model', 'provider', 'user', 'group', 'source', 'config', 'version', 'tool', 'resolution', 'ids', 'q']) {
        expect(FEEDBACK_FILTER_QUERY_PARAMS).toContain(name);
      }
      const cleared = clearedFeedbackQueryParams();
      expect(Object.keys(cleared)).toEqual([...FEEDBACK_FILTER_QUERY_PARAMS]);
      expect(Object.values(cleared).every(value => value === null)).toBe(true);
      expect(FEEDBACK_FILTER_QUERY_PARAMS).not.toContain('sessionId');
    });

    it('does not read an inherited property name as a retired sub-tab', () => {
      expect(parseAdminPath('telemetry', 'constructor')).toEqual(
        { tab: 'telemetry', subTab: 'summary', canonical: ['/admin', 'telemetry'], isCanonical: false });
    });

    it('round-trips the AI Telemetry Groups sub-tab through its path', () => {
      const commands = adminCommands('telemetry', 'groups');
      const parsed = parseAdminPath(commands[1], commands[2] ?? null);

      expect(parsed).toEqual({ tab: 'telemetry', subTab: 'groups', canonical: commands, isCanonical: true });
    });

    it('reads the Services sub-tabs from their slugs', () => {
      for (const [slug, id] of [['gnollhack-wiki', 'gnollhack-wiki'], ['nethack-wiki', 'nethack-wiki'],
        ['knowledge-base', 'knowledge-base'], ['config-health', 'config-health'], ['developer-tools', 'sentry']]) {
        expect(parseAdminPath('services', slug)).toEqual(
          { tab: 'devtools', subTab: id, canonical: ['/admin', 'services', slug], isCanonical: true });
      }
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
      expect(legacy({ tab: 'devtools' })).toEqual(['/admin', 'services']);
      expect(legacy({ tab: 'bogus' })).toEqual(['/admin', 'users']);
    });

    it('maps the old Users sub-tabs', () => {
      expect(legacy({ tab: 'users', subtab: 'users' })).toEqual(['/admin', 'users']);
      expect(legacy({ tab: 'users', subtab: 'deletions' })).toEqual(['/admin', 'users', 'deletions']);
      expect(legacy({ tab: 'users', subtab: 'log' })).toEqual(['/admin', 'users', 'log']);
      expect(legacy({ tab: 'users', subtab: 'bogus' })).toEqual(['/admin', 'users']);
    });

    it('maps the AI Telemetry sub-tabs', () => {
      expect(legacy({ tab: 'telemetry', subtab: 'users' })).toEqual(['/admin', 'telemetry', 'users']);
      expect(legacy({ tab: 'telemetry', subtab: 'models' })).toEqual(['/admin', 'telemetry', 'models']);
      expect(legacy({ tab: 'telemetry', subtab: 'groups' })).toEqual(['/admin', 'telemetry', 'groups']);
      expect(legacy({ tab: 'telemetry', subtab: 'bogus' })).toEqual(['/admin', 'telemetry']);
    });

    it('maps the old Feedback sub-tab and the Feedback tab to /admin/feedback', () => {
      expect(legacy({ tab: 'telemetry', subtab: 'feedback' })).toEqual(['/admin', 'feedback']);
      expect(legacy({ tab: 'feedback' })).toEqual(['/admin', 'feedback']);
      expect(legacy({ tab: 'feedback', subtab: 'gnollbench' })).toEqual(['/admin', 'feedback', 'gnollbench']);
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
    expect(isAdminTabId('services')).toBe(false);
    expect(isAdminTabId('groups')).toBe(false);
    expect(isAdminTabId(null)).toBe(false);
    expect(defaultAdminSubTab('users')).toBe('users');
    expect(defaultAdminSubTab('benchmark')).toBe('run');
    expect(defaultAdminSubTab('telemetry')).toBe('summary');
    expect(defaultAdminSubTab('database')).toBeNull();
  });
});
