<script lang="ts">
  import { themeStore } from '../../stores/theme.svelte';
</script>

<button class="theme-toggle glass glass-interactive" onclick={() => themeStore.toggle()} aria-label="Toggle theme">
  <div class="icon-wrapper" class:is-dark={themeStore.isDark}>
    <!-- Sun -->
    <svg class="sun" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <circle cx="12" cy="12" r="5"/>
      <line x1="12" y1="1" x2="12" y2="3"/>
      <line x1="12" y1="21" x2="12" y2="23"/>
      <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/>
      <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
      <line x1="1" y1="12" x2="3" y2="12"/>
      <line x1="21" y1="12" x2="23" y2="12"/>
      <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/>
      <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
    </svg>
    <!-- Moon -->
    <svg class="moon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
    </svg>
  </div>
</button>

<style>
  .theme-toggle {
    padding: var(--space-2);
    border-radius: var(--radius-sm);
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .icon-wrapper {
    position: relative;
    width: 16px;
    height: 16px;
  }

  .sun, .moon {
    position: absolute;
    inset: 0;
    transition: opacity var(--transition-smooth), transform var(--transition-smooth);
  }

  .sun {
    opacity: 1;
    transform: rotate(0deg) scale(1);
  }

  .moon {
    opacity: 0;
    transform: rotate(-90deg) scale(0.5);
  }

  .is-dark .sun {
    opacity: 0;
    transform: rotate(90deg) scale(0.5);
  }

  .is-dark .moon {
    opacity: 1;
    transform: rotate(0deg) scale(1);
  }
</style>
