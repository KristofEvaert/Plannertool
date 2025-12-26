/**
 * Date utilities for working with YYYY-MM-DD formatted dates
 */

/**
 * Returns today's date as YYYY-MM-DD string
 */
export function todayYmd(): string {
  return toYmd(new Date());
}

/**
 * Converts a Date object to YYYY-MM-DD string
 */
export function toYmd(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/**
 * Parses a YYYY-MM-DD string to a Date object, or null if invalid
 */
export function parseYmd(ymd: string): Date | null {
  const parsed = new Date(ymd + 'T00:00:00');
  if (isNaN(parsed.getTime())) {
    return null;
  }
  return parsed;
}

/**
 * Adds days to a YYYY-MM-DD date string and returns a new YYYY-MM-DD string
 */
export function addDaysYmd(ymd: string, delta: number): string {
  const date = parseYmd(ymd);
  if (!date) {
    throw new Error(`Invalid date string: ${ymd}`);
  }
  date.setDate(date.getDate() + delta);
  return toYmd(date);
}

