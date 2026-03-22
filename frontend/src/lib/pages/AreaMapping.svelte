<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import AreaMapper from '../components/area/AreaMapper.svelte';
  import AreaControls from '../components/area/AreaControls.svelte';
  import type { Profile } from '../types/settings';

  let { profile }: { profile: Profile } = $props();

  const demoAbsoluteMode = {
    display: { width: 1920, height: 1080, x: 960, y: 540, rotation: 0 },
    tablet: { width: 152.0, height: 95.0, x: 76.0, y: 47.5, rotation: 0 },
    enableClipping: true,
    enableAreaLimiting: false,
    lockAspectRatio: false,
  };

  let absoluteMode = $derived(profile.absoluteModeSettings ?? demoAbsoluteMode);
</script>

<div class="area-mapping">
  <div class="mapping-layout">
    <GlassPanel class="mapper-panel" heavy>
      <AreaMapper {absoluteMode} />
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
    </div>
  </div>
</div>

<style>
  .area-mapping {
    max-width: 1200px;
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
