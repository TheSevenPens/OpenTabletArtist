export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

export function aspectRatio(width: number, height: number): number {
  return width / height;
}

export function constrainToAspectRatio(
  width: number,
  height: number,
  targetRatio: number,
  anchor: 'width' | 'height' = 'width'
): { width: number; height: number } {
  if (anchor === 'width') {
    return { width, height: width / targetRatio };
  }
  return { width: height * targetRatio, height };
}

export function formatMm(value: number): string {
  return `${value.toFixed(1)} mm`;
}

export function formatPx(value: number): string {
  return `${Math.round(value)} px`;
}
