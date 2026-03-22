<script lang="ts">
  import type { AbsoluteModeSettings } from '../../types/settings';
  import type { DigitizerSpecs } from '../../types/tablet';

  let {
    absoluteMode,
    digitizer,
  }: {
    absoluteMode: AbsoluteModeSettings;
    digitizer?: DigitizerSpecs;
  } = $props();

  // SVG viewBox dimensions
  const viewW = 800;
  const viewH = 500;
  const padding = 40;

  // Compute proportional rectangles
  function computeDisplayRect() {
    const d = absoluteMode.display;
    const maxW = (viewW - padding * 2) * 0.45;
    const maxH = viewH - padding * 2;
    const scale = Math.min(maxW / d.width, maxH / d.height);
    const w = d.width * scale;
    const h = d.height * scale;
    return {
      x: padding + (maxW - w) / 2,
      y: padding + (maxH - h) / 2,
      width: w,
      height: h,
    };
  }

  function computeTabletRect() {
    const t = absoluteMode.tablet;
    const fullW = digitizer?.width ?? t.width;
    const fullH = digitizer?.height ?? t.height;
    const maxW = (viewW - padding * 2) * 0.45;
    const maxH = viewH - padding * 2;
    const scale = Math.min(maxW / fullW, maxH / fullH);
    const offsetX = viewW * 0.5 + padding;

    // Full tablet outline
    const fullRect = {
      x: offsetX + (maxW - fullW * scale) / 2,
      y: padding + (maxH - fullH * scale) / 2,
      width: fullW * scale,
      height: fullH * scale,
    };

    // Active area within tablet
    const activeW = t.width * scale;
    const activeH = t.height * scale;
    const activeX = fullRect.x + (t.x - t.width / 2) * scale + fullRect.width / 2;
    const activeY = fullRect.y + (t.y - t.height / 2) * scale + fullRect.height / 2;

    return { full: fullRect, active: { x: activeX, y: activeY, width: activeW, height: activeH } };
  }

  let displayRect = $derived(computeDisplayRect());
  let tabletRect = $derived(computeTabletRect());
</script>

<div class="area-mapper">
  <svg viewBox="0 0 {viewW} {viewH}" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <!-- Display gradient -->
      <linearGradient id="displayGrad" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" stop-color="var(--accent)" stop-opacity="0.15" />
        <stop offset="100%" stop-color="var(--accent)" stop-opacity="0.05" />
      </linearGradient>
      <!-- Tablet gradient -->
      <linearGradient id="tabletGrad" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" stop-color="var(--success)" stop-opacity="0.15" />
        <stop offset="100%" stop-color="var(--success)" stop-opacity="0.05" />
      </linearGradient>
      <!-- Grid pattern -->
      <pattern id="grid" width="20" height="20" patternUnits="userSpaceOnUse">
        <path d="M 20 0 L 0 0 0 20" fill="none" stroke="var(--divider)" stroke-width="0.5"/>
      </pattern>
    </defs>

    <!-- Background grid -->
    <rect width={viewW} height={viewH} fill="url(#grid)" opacity="0.5" rx="12"/>

    <!-- Labels -->
    <text x={padding} y={padding - 12} fill="var(--text-secondary)" font-size="12" font-family="var(--font-sans)">Display</text>
    <text x={viewW * 0.5 + padding} y={padding - 12} fill="var(--text-secondary)" font-size="12" font-family="var(--font-sans)">Tablet</text>

    <!-- Display rectangle -->
    <rect
      x={displayRect.x}
      y={displayRect.y}
      width={displayRect.width}
      height={displayRect.height}
      fill="url(#displayGrad)"
      stroke="var(--accent)"
      stroke-width="2"
      rx="4"
    />

    <!-- Display dimensions label -->
    <text
      x={displayRect.x + displayRect.width / 2}
      y={displayRect.y + displayRect.height + 18}
      fill="var(--text-muted)"
      font-size="11"
      font-family="var(--font-sans)"
      text-anchor="middle"
    >
      {absoluteMode.display.width.toFixed(0)} x {absoluteMode.display.height.toFixed(0)}
    </text>

    <!-- Tablet full outline -->
    <rect
      x={tabletRect.full.x}
      y={tabletRect.full.y}
      width={tabletRect.full.width}
      height={tabletRect.full.height}
      fill="none"
      stroke="var(--text-muted)"
      stroke-width="1"
      stroke-dasharray="4 4"
      rx="4"
    />

    <!-- Tablet active area -->
    <rect
      x={tabletRect.active.x}
      y={tabletRect.active.y}
      width={tabletRect.active.width}
      height={tabletRect.active.height}
      fill="url(#tabletGrad)"
      stroke="var(--success)"
      stroke-width="2"
      rx="4"
    />

    <!-- Tablet active area dimensions -->
    <text
      x={tabletRect.full.x + tabletRect.full.width / 2}
      y={tabletRect.full.y + tabletRect.full.height + 18}
      fill="var(--text-muted)"
      font-size="11"
      font-family="var(--font-sans)"
      text-anchor="middle"
    >
      {absoluteMode.tablet.width.toFixed(1)} x {absoluteMode.tablet.height.toFixed(1)} mm
    </text>

    <!-- Mapping arrow -->
    <line
      x1={displayRect.x + displayRect.width + 15}
      y1={viewH / 2}
      x2={tabletRect.full.x - 15}
      y2={viewH / 2}
      stroke="var(--text-muted)"
      stroke-width="1"
      stroke-dasharray="6 4"
      marker-end="url(#arrowhead)"
    />

    <defs>
      <marker id="arrowhead" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto">
        <polygon points="0 0, 8 3, 0 6" fill="var(--text-muted)"/>
      </marker>
    </defs>
  </svg>
</div>

<style>
  .area-mapper {
    width: 100%;
    aspect-ratio: 8 / 5;
    max-height: 500px;
  }

  svg {
    width: 100%;
    height: 100%;
    border-radius: var(--radius-lg);
  }
</style>
