import { ParamMap, UrlMatchResult, UrlSegment } from '@angular/router';
import type { BenchmarkSubTab } from './benchmark/state/benchmark-shell-bridge.service';

/** The admin dashboard's top-level tabs, in display order. */
export type AdminTabId = 'users' | 'configs' | 'database' | 'telemetry' | 'security' | 'benchmark' | 'devtools' | 'feedback';

/** One sub-tab: its code id and its URL slug. The default sub-tab's slug is empty. */
export type AdminSubTabSlug = readonly [id: string, slug: string];

/** Tab id → URL slug. The address bar shows the visible name, so GnollBench is `gnollbench`. */
export const ADMIN_TAB_SLUGS: Readonly<Record<AdminTabId, string>> = {
  users: 'users',
  configs: 'configs',
  database: 'database',
  telemetry: 'telemetry',
  security: 'security',
  benchmark: 'gnollbench',
  devtools: 'services',
  feedback: 'feedback'
};

/**
 * Slugs a tab had before it was renamed, so old bookmarks and alerts still open it. Such an address is
 * accepted and replaced by the canonical one; a retired slug is never reused for another tab.
 */
export const RETIRED_ADMIN_TAB_SLUGS: Readonly<Record<string, AdminTabId>> = { devtools: 'devtools' };

/** Sub-tab slugs that became top-level tabs; the old path opens the tab. Never reused as a sub-tab slug. */
export const RETIRED_ADMIN_SUBTABS: Readonly<Partial<Record<AdminTabId, Readonly<Record<string, AdminTabId>>>>> =
  { telemetry: { feedback: 'feedback' } };

/** The tab a retired sub-tab slug of `tab` became, or undefined; never an inherited property. */
function retiredSubTabTarget(tab: AdminTabId, subSlug: string | null): AdminTabId | undefined {
  const retired = RETIRED_ADMIN_SUBTABS[tab];
  return retired && subSlug != null && Object.hasOwn(retired, subSlug) ? retired[subSlug] : undefined;
}

/** Every GnollBench sub-tab needs a slug; a new `BenchmarkSubTab` without one fails to compile. */
const BENCHMARK_SUBTAB_SLUGS: Readonly<Record<BenchmarkSubTab, string>> = {
  run: '',
  history: 'history',
  multirun: 'multi-run',
  multisuite: 'multi-suite',
  suites: 'suites',
  profiles: 'profiles',
  modelcomparison: 'model-comparison',
  chatconsistency: 'chat-consistency'
};

/** Each tab's sub-tabs in order, as [id, slug]; the first is the default and has no path segment. */
export const ADMIN_SUBTAB_SLUGS: Readonly<Partial<Record<AdminTabId, readonly AdminSubTabSlug[]>>> = {
  users: [['users', ''], ['groups', 'groups'], ['deletions', 'deletions'], ['log', 'log']],
  configs: [['', ''], ['api-keys', 'api-keys']],
  telemetry: [['summary', ''], ['models', 'models'], ['users', 'users'], ['groups', 'groups']],
  security: [['summary', ''], ['events', 'events'], ['settings', 'settings']],
  benchmark: Object.entries(BENCHMARK_SUBTAB_SLUGS) as [BenchmarkSubTab, string][],
  devtools: [['source', ''], ['gnollhack-wiki', 'gnollhack-wiki'], ['nethack-wiki', 'nethack-wiki'],
             ['knowledge-base', 'knowledge-base'], ['config-health', 'config-health'], ['sentry', 'developer-tools']],
  feedback: [['overseer', ''], ['gnollbench', 'gnollbench']]
};

/**
 * Every filter parameter of the Feedback tab's sub-tabs, which share some names (`status`, `resolvedVia`,
 * `category`, `model`, `provider`, `group`, `q`). A sub-tab switch removes them all, so none leaks into the
 * other sub-tab.
 */
export const FEEDBACK_FILTER_QUERY_PARAMS: readonly string[] = [
  'status', 'resolvedVia', 'category', 'model', 'provider', 'user', 'group', 'source', 'config', 'version', 'tool', 'resolution', 'ids', 'q',
  'accepted', 'run', 'series', 'battery', 'batch', 'suite', 'question', 'condition', 'corpus', 'variant', 'harness', 'fingerprints'
];

/** Query parameters that remove every Feedback filter parameter, for a navigation with `queryParamsHandling: 'merge'`. */
export function clearedFeedbackQueryParams(): Record<string, null> {
  return Object.fromEntries(FEEDBACK_FILTER_QUERY_PARAMS.map(name => [name, null]));
}

const TAB_IDS = Object.keys(ADMIN_TAB_SLUGS) as AdminTabId[];

/** True when `id` names an admin tab. */
export function isAdminTabId(id: string | null | undefined): id is AdminTabId {
  return id != null && (TAB_IDS as string[]).includes(id);
}

/** The id of `tab`'s default sub-tab, or null for a tab without sub-tabs. */
export function defaultAdminSubTab(tab: AdminTabId): string | null {
  return ADMIN_SUBTAB_SLUGS[tab]?.[0][0] ?? null;
}

/** Matches `admin`, `admin/:tab` and `admin/:tab/:subtab` as one route, so a tab change reuses the page. */
export function adminUrlMatcher(segments: UrlSegment[]): UrlMatchResult | null {
  if (segments.length < 1 || segments.length > 3 || segments[0].path !== 'admin') {
    return null;
  }
  const posParams: Record<string, UrlSegment> = {};
  if (segments.length > 1) posParams['tab'] = segments[1];
  if (segments.length > 2) posParams['subtab'] = segments[2];
  return { consumed: segments, posParams };
}

/** Router commands for `tab` and `subTab`; the default or an unknown sub-tab adds no segment. */
export function adminCommands(tab: AdminTabId, subTab?: string | null): string[] {
  const commands = ['/admin', ADMIN_TAB_SLUGS[tab]];
  const slug = ADMIN_SUBTAB_SLUGS[tab]?.find(([id]) => id === subTab)?.[1];
  if (slug) commands.push(slug);
  return commands;
}

/**
 * The tab and sub-tab a path names, and its canonical commands. A tab is found by its slug or by a
 * retired slug; an unknown tab is Users, an unknown sub-tab is the tab's default, a sub-tab named
 * by its id (`users/users`, `services/sentry`) is accepted, and a retired sub-tab opens the tab it became.
 */
export function parseAdminPath(tabSlug: string | null, subSlug: string | null):
  { tab: AdminTabId; subTab: string | null; canonical: string[]; isCanonical: boolean } {
  const tab = TAB_IDS.find(id => ADMIN_TAB_SLUGS[id] === tabSlug) ??
    (tabSlug != null && Object.hasOwn(RETIRED_ADMIN_TAB_SLUGS, tabSlug) ? RETIRED_ADMIN_TAB_SLUGS[tabSlug] : undefined) ?? 'users';
  const promoted = retiredSubTabTarget(tab, subSlug);
  if (promoted) {
    const subTab = defaultAdminSubTab(promoted);
    return { tab: promoted, subTab, canonical: adminCommands(promoted, subTab), isCanonical: false };
  }
  const entries = ADMIN_SUBTAB_SLUGS[tab];
  let subTab: string | null = null;
  if (entries) {
    const named = subSlug == null
      ? undefined
      : entries.find(([, slug]) => slug !== '' && slug === subSlug) ?? entries.find(([id]) => id !== '' && id === subSlug);
    subTab = (named ?? entries[0])[0];
  }
  const canonical = adminCommands(tab, subTab);
  const actual = ['/admin', ...(tabSlug != null ? [tabSlug] : []), ...(subSlug != null ? [subSlug] : [])];
  const isCanonical = actual.length === canonical.length && actual.every((part, i) => part === canonical[i]);
  return { tab, subTab, canonical, isCanonical };
}

/**
 * A list kept in one query parameter as comma-separated values (`status=new,regressed`), blanks
 * dropped; null when the parameter is absent.
 */
export function readAdminQueryList(query: ParamMap, name: string): string[] | null {
  const raw = query.get(name);
  if (raw == null) return null;
  return raw.split(',').map(value => value.trim()).filter(value => value !== '');
}

/** A list as one comma-separated query value; null, which removes the parameter, for an empty list. */
export function adminQueryList(values: readonly string[]): string | null {
  return values.length > 0 ? values.join(',') : null;
}

/**
 * The path an old `/admin?tab=…&subtab=…` link names, or null when it carries no `tab`. The retired
 * `groups` and `apikeys` tabs map to Users → Groups and System Configs → API Keys, and a retired
 * sub-tab to the tab it became.
 */
export function legacyAdminPath(query: ParamMap): string[] | null {
  const tab = query.get('tab');
  if (tab == null) return null;
  if (tab === 'groups') return adminCommands('users', 'groups');
  if (tab === 'apikeys') return adminCommands('configs', 'api-keys');
  if (!isAdminTabId(tab)) return adminCommands('users');
  const promoted = retiredSubTabTarget(tab, query.get('subtab'));
  if (promoted) return adminCommands(promoted);
  return adminCommands(tab, query.get('subtab'));
}
