<script lang="ts">
  import type { Snippet } from 'svelte';

  let {
    icon,
    label,
    href,
    active = false,
  }: {
    icon: Snippet;
    label: string;
    href: string;
    active?: boolean;
  } = $props();
</script>

<a
  {href}
  class="nav-item glass-interactive"
  class:active
  aria-current={active ? 'page' : undefined}
>
  <span class="nav-icon">
    {@render icon()}
  </span>
  <span class="nav-label">{label}</span>
  {#if active}
    <div class="active-indicator"></div>
  {/if}
</a>

<style>
  .nav-item {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-2) var(--space-4);
    border-radius: var(--radius-md);
    color: var(--text-secondary);
    text-decoration: none;
    position: relative;
    transition: all var(--transition-smooth);
    border: 1px solid transparent;
  }

  .nav-item:hover {
    color: var(--text-primary);
    background: var(--glass-bg-hover);
  }

  .nav-item.active {
    color: var(--accent);
    background: var(--accent-muted);
    border-color: rgba(129, 140, 248, 0.15);
  }

  .nav-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 20px;
    height: 20px;
    flex-shrink: 0;
  }

  .nav-icon :global(svg) {
    width: 18px;
    height: 18px;
  }

  .nav-label {
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
  }

  .active-indicator {
    position: absolute;
    left: 0;
    top: 50%;
    transform: translateY(-50%);
    width: 3px;
    height: 60%;
    background: var(--accent);
    border-radius: var(--radius-full);
    box-shadow: var(--accent-glow);
  }
</style>
