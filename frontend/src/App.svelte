<script lang="ts">
  import Shell from './lib/components/layout/Shell.svelte';
  import Dashboard from './lib/pages/Dashboard.svelte';
  import AreaMapping from './lib/pages/AreaMapping.svelte';
  import Bindings from './lib/pages/Bindings.svelte';
  import Filters from './lib/pages/Filters.svelte';
  import Console from './lib/pages/Console.svelte';
  import About from './lib/pages/About.svelte';
  import { themeStore } from './lib/stores/theme.svelte';
  import { fetchTablets, fetchSettings } from './lib/services/api';
  import { tabletsStore } from './lib/stores/tablets.svelte';
  import { settingsStore } from './lib/stores/settings.svelte';
  import { connectionStore } from './lib/stores/connection.svelte';

  let hash = $state(location.hash || '#/');

  function onHashChange() {
    hash = location.hash || '#/';
  }

  // Derive the current route href for nav highlighting
  let currentRoute = $derived(hash || '#/');

  async function loadDaemonData() {
    try {
      connectionStore.set('connecting');
      const [tablets, settings] = await Promise.all([
        fetchTablets(),
        fetchSettings(),
      ]);
      tabletsStore.set(tablets);
      settingsStore.set(settings);
      connectionStore.set('connected');
    } catch {
      connectionStore.set('disconnected');
      // Retry in 5s
      setTimeout(loadDaemonData, 5000);
    }
  }

  $effect(() => {
    void themeStore.current;
    window.addEventListener('hashchange', onHashChange);
    loadDaemonData();
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
