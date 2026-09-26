/** A job or series status as the UI shows it. The server still sends the British `Cancelled`. */
export function jobStatusLabel(status: string | null | undefined): string {
  return status === 'Cancelled' ? 'Canceled' : status ?? '';
}
