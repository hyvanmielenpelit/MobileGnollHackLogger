import { BenchmarkGameSnapshotDto } from '../../services/admin-benchmark.service';

/* A snapshot shaped like a real one: prose, a legend line naming the hero, the map block the
   way dump_map_ai() writes it, then filler to 250 lines. The hero '@' is at <10,13>. */
export function buildBoard(): string {
  const lines = ['Map:', 'The hero is at <10,13>, shown as \'@\'.', 'A food ration lies here.', 'Map grid:'];
  let tens = '    ';
  let units = '    ';
  for (let x = 1; x < 80; x++) {
    tens += x % 10 === 0 ? String(x / 10) : ' ';
    units += String(x % 10);
  }
  lines.push(tens.trimEnd(), units);
  for (let y = 0; y <= 20; y++) {
    const gutter = (y < 10 ? ' ' : '') + y + ': ';
    const cells = y === 13 ? '---------@....%....|' : '  |....|';
    lines.push((gutter + cells).trimEnd());
  }
  lines.push('', 'Inventory:', 'a - 2 food rations');
  while (lines.length < 250) lines.push(`filler ${lines.length + 1}`);
  return lines.join('\n');
}

export function snapshotWith(text: string, extra: Partial<BenchmarkGameSnapshotDto> = {}): BenchmarkGameSnapshotDto {
  return {
    id: 1,
    name: 'Emergency Low HP',
    charCount: text.length,
    sha256: 'abc1234567890',
    captureMethod: 'client_refresh_snapshot',
    sanitizedText: text,
    createdAtUtc: new Date().toISOString(),
    ...extra
  } as BenchmarkGameSnapshotDto;
}
