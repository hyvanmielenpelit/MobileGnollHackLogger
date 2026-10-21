import {
  REPORT_PACK_STORAGE_KEY,
  REPORT_WRITER_MEMORY_KEY,
  rememberReportWriterId,
  rememberedReportWriterId
} from './report-writer-memory';

describe('report-writer-memory', () => {
  beforeEach(() => {
    localStorage.removeItem(REPORT_WRITER_MEMORY_KEY);
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
  });

  afterEach(() => {
    localStorage.removeItem(REPORT_WRITER_MEMORY_KEY);
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
  });

  it('remembers nothing at first', () => {
    expect(rememberedReportWriterId()).toBeNull();
  });

  it('reads back the writer it remembered, as a versioned record', () => {
    rememberReportWriterId(8);
    expect(rememberedReportWriterId()).toBe(8);
    expect(JSON.parse(localStorage.getItem(REPORT_WRITER_MEMORY_KEY)!)).toEqual({ version: 1, writerConfigId: 8 });

    rememberReportWriterId(3);
    expect(rememberedReportWriterId()).toBe(3);
  });

  it('reads a corrupt or unknown record as nothing remembered, without falling back to the wizard', () => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8 }));

    localStorage.setItem(REPORT_WRITER_MEMORY_KEY, '{not json');
    expect(rememberedReportWriterId()).toBeNull();
    localStorage.setItem(REPORT_WRITER_MEMORY_KEY, JSON.stringify({ version: 2, writerConfigId: 8 }));
    expect(rememberedReportWriterId()).toBeNull();
    localStorage.setItem(REPORT_WRITER_MEMORY_KEY, JSON.stringify({ version: 1, writerConfigId: '8' }));
    expect(rememberedReportWriterId()).toBeNull();
    localStorage.setItem(REPORT_WRITER_MEMORY_KEY, JSON.stringify({ version: 1, writerConfigId: 0 }));
    expect(rememberedReportWriterId()).toBeNull();
    localStorage.setItem(REPORT_WRITER_MEMORY_KEY, 'null');
    expect(rememberedReportWriterId()).toBeNull();
  });

  it('falls back to the wizard\'s stored writer while nothing is remembered, and prefers its own record once written', () => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8, sidebarWidth: 448 }));
    expect(rememberedReportWriterId()).toBe(8);

    rememberReportWriterId(3);
    expect(rememberedReportWriterId()).toBe(3);
    // The wizard's record is left as it was.
    expect(JSON.parse(localStorage.getItem(REPORT_PACK_STORAGE_KEY)!)).toEqual({ writerConfigId: 8, sidebarWidth: 448 });
  });

  it('reads an unreadable wizard record, or one without a writer, as nothing remembered', () => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, '{not json');
    expect(rememberedReportWriterId()).toBeNull();
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ sidebarWidth: 448 }));
    expect(rememberedReportWriterId()).toBeNull();
  });

  it('remembers nothing, and throws nothing, while storage is unavailable', () => {
    vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
      throw new Error('Storage is disabled.');
    });
    vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
      throw new Error('Storage is disabled.');
    });

    expect(() => rememberReportWriterId(8)).not.toThrow();
    expect(rememberedReportWriterId()).toBeNull();
  });

  it('ignores an id that is not a configuration\'s', () => {
    rememberReportWriterId(0);
    rememberReportWriterId(-4);
    rememberReportWriterId(Number.NaN);
    expect(localStorage.getItem(REPORT_WRITER_MEMORY_KEY)).toBeNull();
  });
});
