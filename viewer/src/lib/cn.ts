import { clsx, type ClassValue } from 'clsx';

/** Joins class names, dropping falsy ones. */
export function cn(...values: ClassValue[]): string {
  return clsx(values);
}

/** `1 type`, `2 types`. */
export function plural(count: number, noun: string, pluralNoun = `${noun}s`): string {
  return `${count} ${count === 1 ? noun : pluralNoun}`;
}

/** The last segment of a dotted id: `Orders.Api.OrdersController` -> `OrdersController`. */
export function shortName(id: string): string {
  const dot = id.lastIndexOf('.');
  return dot >= 0 ? id.slice(dot + 1) : id;
}

/** The namespace part of a dotted id. */
export function namespaceOf(id: string): string {
  const dot = id.lastIndexOf('.');
  return dot >= 0 ? id.slice(0, dot) : '';
}

/** The file name part of a path. */
export function baseName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}
