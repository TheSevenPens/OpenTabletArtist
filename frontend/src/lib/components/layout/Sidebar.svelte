<script lang="ts">
  import NavItem from './NavItem.svelte';

  let { currentRoute = '' }: { currentRoute?: string } = $props();

  const navItems = [
    { label: 'Dashboard', href: '#/', icon: 'home' },
    { label: 'Tablet Settings', href: '#/tablets', icon: 'users' },
    { label: 'Settings Snapshots', href: '#/presets', icon: 'save' },
    { label: 'Console', href: '#/console', icon: 'terminal' },
    { label: 'About', href: '#/about', icon: 'info' },
  ];
</script>

<aside class="sidebar glass-heavy">
  <div class="sidebar-header">
    <div class="logo">
      <svg width="28" height="28" viewBox="0 0 28 28" fill="none">
        <rect width="28" height="28" rx="8" fill="var(--accent)"/>
        <path d="M8 10h12v2H8zm0 3h8v2H8zm0 3h10v2H8z" fill="var(--text-inverse)" opacity="0.9"/>
      </svg>
    </div>
    <div class="brand">
      <span class="brand-name">OpenTabletDriver</span>
      <span class="brand-tag">UX Prototype</span>
    </div>
  </div>

  <nav class="nav-list">
    {#each navItems as item}
      <NavItem
        label={item.label}
        href={item.href}
        active={item.href === '#/' ? currentRoute === '#/' : currentRoute.startsWith(item.href)}
      >
        {#snippet icon()}
          {#if item.icon === 'home'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
          {:else if item.icon === 'users'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
          {:else if item.icon === 'save'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>
          {:else if item.icon === 'map'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><rect x="7" y="7" width="6" height="6" rx="1" opacity="0.6"/></svg>
          {:else if item.icon === 'zap'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>
          {:else if item.icon === 'filter'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/></svg>
          {:else if item.icon === 'terminal'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/></svg>
          {:else if item.icon === 'info'}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
          {/if}
        {/snippet}
      </NavItem>
    {/each}
  </nav>

  <div class="sidebar-footer">
    <div class="version-badge glass-subtle">
      <span>v0.1.0-proto</span>
    </div>
  </div>
</aside>

<style>
  .sidebar {
    grid-area: sidebar;
    display: flex;
    flex-direction: column;
    padding: var(--space-4);
    gap: var(--space-2);
    border-radius: 0;
    border-right: var(--glass-border-width) solid var(--glass-border);
    border-top: none;
    border-bottom: none;
    border-left: none;
    overflow-y: auto;
  }

  .sidebar-header {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-3) var(--space-2);
    margin-bottom: var(--space-2);
  }

  .logo {
    flex-shrink: 0;
  }

  .brand {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .brand-name {
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    line-height: var(--line-height-tight);
  }

  .brand-tag {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    line-height: var(--line-height-tight);
  }

  .nav-list {
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
    flex: 1;
  }

  .sidebar-footer {
    padding-top: var(--space-3);
    border-top: 1px solid var(--divider);
  }

  .version-badge {
    padding: var(--space-1) var(--space-3);
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    text-align: center;
  }
</style>
