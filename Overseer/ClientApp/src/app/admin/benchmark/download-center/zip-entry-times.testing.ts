/**
 * Spec helper: the modification time of every entry of a zip, read from its central directory.
 * `fflate`'s `unzipSync` returns only the bytes, so the DOS date and time fields are decoded here:
 * local time, with two-second resolution. Imported by specs only.
 */
export function zipEntryTimes(zip: Uint8Array): Map<string, Date> {
  const view = new DataView(zip.buffer, zip.byteOffset, zip.byteLength);
  let end = zip.byteLength - 22;
  while (end >= 0 && view.getUint32(end, true) !== 0x06054b50) {
    end--;
  }
  if (end < 0) {
    throw new Error('No end-of-central-directory record.');
  }
  const count = view.getUint16(end + 10, true);
  let offset = view.getUint32(end + 16, true);
  const times = new Map<string, Date>();
  for (let i = 0; i < count; i++) {
    if (view.getUint32(offset, true) !== 0x02014b50) {
      throw new Error('Bad central directory header.');
    }
    const time = view.getUint16(offset + 12, true);
    const date = view.getUint16(offset + 14, true);
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const name = new TextDecoder().decode(zip.subarray(offset + 46, offset + 46 + nameLength));
    times.set(name, new Date(
      (date >> 9) + 1980, ((date >> 5) & 15) - 1, date & 31,
      time >> 11, (time >> 5) & 63, (time & 31) * 2));
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return times;
}
