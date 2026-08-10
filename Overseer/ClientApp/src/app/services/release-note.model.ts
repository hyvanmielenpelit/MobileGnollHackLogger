export interface ReleaseHighlight {
  type: 'feature' | 'fix' | 'improvement' | 'security';
  text: string;
}

export interface ReleaseNote {
  version: string;
  date: string;
  summary: string;
  highlights: ReleaseHighlight[];
}
