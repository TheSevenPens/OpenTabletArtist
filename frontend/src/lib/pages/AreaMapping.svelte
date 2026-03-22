<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import AreaMapper from '../components/area/AreaMapper.svelte';
  import AreaControls from '../components/area/AreaControls.svelte';
  import { settingsStore } from '../stores/settings.svelte';
  import { tabletsStore } from '../stores/tablets.svelte';

  // Demo fallback data for when no daemon is connected
  const demoAbsoluteMode = {
    display: { width: 1920, height: 1080, x: 960, y: 540, rotation: 0 },
    tablet: { width: 152.0, height: 95.0, x: 76.0, y: 47.5, rotation: 0 },
    enableClipping: true,
    enableAreaLimiting: false,
    lockAspectRatio: false,
  };

  const demoDigitizer = { width: 152.0, height: 95.0, maxX: 32767, maxY: 32767 };

  let absoluteMode = $derived(
    settingsStore.activeProfile?.absoluteModeSettings ?? demoAbsoluteMode
  );
  let digitizer = $derived(
    tabletsStore.current?.specifications.digitizer ?? demoDigitizer
  );
</script>

<div class="area-mapping">
  <header class="page-header">
    <h1 class="page-title">Area Mapping</h1>
    <p class="page-subtitle">Configure how your tablet maps to your display</p>
  </header>

  <div class="mapping-layout">
    <GlassPanel class="mapper-panel" heavy>
      <AreaMapper {absoluteMode} {digitizer} />
    </GlassPanel>

    <div class="controls-sidebar">
      <AreaControls
        label="Display"
        area={absoluteMode.display}
        color="var(--accent)"
      />
      <AreaControls
        label="Tablet"
        area={absoluteMode.tablet}
        locked={absoluteMode.lockAspectRatio}
        color="var(--success)"
      />

      <GlassPanel padding="var(--space-4)">
        <div class="options">
          <label class="option">
            <input type="checkbox" checked={absoluteMode.lockAspectRatio} />
            <span>Force proportions</span>
          </label>
        </div>
      </GlassPanel>

      <!-- Advanced settings (clipping, area limiting, rotation) hidden from default view.
           These remain enabled in the settings model but are not exposed to normal users.
           TODO: Add an "Advanced" toggle to reveal these for power users. -->
    </div>
  </div>
</div>

<style>
  .area-mapping {
    max-width: 1200px;
  }

  .page-header {
    margin-bottom: var(--space-7);
  }

  .page-title {
    font-size: var(--font-size-2xl);
    font-weight: var(--font-weight-bold);
    color: var(--text-primary);
    margin: 0 0 var(--space-1) 0;
  }

  .page-subtitle {
    font-size: var(--font-size-base);
    color: var(--text-secondary);
    margin: 0;
  }

  .mapping-layout {
    display: grid;
    grid-template-columns: 1fr 280px;
    gap: var(--space-5);
    align-items: start;
  }

  .controls-sidebar {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }

  .options {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
  }

  .option {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    font-size: var(--font-size-sm);
    color: var(--text-secondary);
    cursor: pointer;
  }

  .option input[type="checkbox"] {
    width: 16px;
    height: 16px;
    accent-color: var(--accent);
    cursor: pointer;
  }

  .option:hover {
    color: var(--text-primary);
  }
</style>
