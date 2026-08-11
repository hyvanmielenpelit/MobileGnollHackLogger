export interface ReleaseChange {
  type: 'feature' | 'fix' | 'improvement' | 'security';
  text: string;
}

export interface ReleaseNote {
  version: string;
  date: string;
  summary: string;
  changes: ReleaseChange[];
}

export interface ChangelogResponse {
  pageSize: number;
  notes: ReleaseNote[];
}
