<script lang="ts">
  import Shell from './lib/components/layout/Shell.svelte';
  import Dashboard from './lib/pages/Dashboard.svelte';
  import AreaMapping from './lib/pages/AreaMapping.svelte';
  import Bindings from './lib/pages/Bindings.svelte';
  import Filters from './lib/pages/Filters.svelte';
  import Console from './lib/pages/Console.svelte';
  import About from './lib/pages/About.svelte';
  import { themeStore } from './lib/stores/theme.svelte';

  let hash = $state(location.hash || '#/');

  function onHashChange() {
    hash = location.hash || '#/';
  }

  // Derive the current route href for nav highlighting
  let currentRoute = $derived(hash || '#/');

  // Initialize theme on mount
  $effect(() => {
    // Theme store auto-applies via its own effect
    void themeStore.current;
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  });
</script>

<Shell {currentRoute}>
  {#if hash === '#/area'}
    <AreaMapping />
  {:else if hash === '#/bindings'}
    <Bindings />
  {:else if hash === '#/filters'}
    <Filters />
  {:else if hash === '#/console'}
    <Console />
  {:else if hash === '#/about'}
    <About />
  {:else}
    <Dashboard />
  {/if}
</Shell>
