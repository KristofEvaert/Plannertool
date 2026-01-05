import { DatePicker } from 'primeng/datepicker';

const calendarProto = (DatePicker as unknown as { prototype?: any })?.prototype;

if (calendarProto) {
  if (typeof window !== 'undefined' && !calendarProto.window) {
    calendarProto.window = window;
  }

  const originalUpdateFocus = calendarProto.updateFocus;
  if (typeof originalUpdateFocus === 'function') {
    calendarProto.updateFocus = function (...args: unknown[]) {
      if (!this?.contentViewChild?.nativeElement) {
        return;
      }
      return originalUpdateFocus.apply(this, args);
    };
  }
}
