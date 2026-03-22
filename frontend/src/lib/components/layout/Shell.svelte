<script lang="ts">
  import type { Snippet } from 'svelte';
  import Sidebar from './Sidebar.svelte';
  import StatusBar from './StatusBar.svelte';

  let {
    children,
    currentRoute = '',
  }: {
    children: Snippet;
    currentRoute?: string;
  } = $props();
</script>

<div class="shell">
  <Sidebar {currentRoute} />
  <main class="main-content">
    <div class="content-scroll">
      {@render children()}
    </div>
  </main>
  <StatusBar />
</div>

<style>
  .shell {
    display: grid;
    grid-template-columns: var(--sidebar-width) 1fr;
    grid-template-rows: 1fr var(--statusbar-height);
    grid-template-areas:
      "sidebar main"
      "status  status";
    height: 100vh;
    width: 100vw;
    overflow: hidden;
    background: var(--bg-base);
    transition: background var(--transition-theme);
  }

  .main-content {
    grid-area: main;
    overflow: hidden;
    position: relative;
  }

  .content-scroll {
    height: 100%;
    overflow-y: auto;
    padding: var(--space-7);
  }
</style>
