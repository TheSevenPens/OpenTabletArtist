<script lang="ts">
  import Shell from './lib/components/layout/Shell.svelte';
  import Dashboard from './lib/pages/Dashboard.svelte';
  import Profiles from './lib/pages/Profiles.svelte';
  import TabletDetail from './lib/pages/TabletDetail.svelte';
  import Presets from './lib/pages/Presets.svelte';
  import Console from './lib/pages/Console.svelte';
  import About from './lib/pages/About.svelte';
  import { themeStore } from './lib/stores/theme.svelte';
  import { fetchTablets, fetchSettings, fetchVMultiStatus } from './lib/services/api';
  import { tabletsStore } from './lib/stores/tablets.svelte';
  import { settingsStore } from './lib/stores/settings.svelte';
  import { connectionStore } from './lib/stores/connection.svelte';
  import { vmultiStore } from './lib/stores/vmulti.svelte';

  let hash = $state(location.hash || '#/');

  function onHashChange() {
    hash = location.hash || '#/';
  }

  let currentRoute = $derived(hash || '#/');

  // Parse tablet detail routes: #/tablets/{name}/{subTab}
  let tabletRoute = $derived.by(() => {
    const match = hash.match(/^#\/tablets\/([^/]+)\/?(area|bindings|filters)?$/);
    if (!match) return null;
    return {
      tabletName: decodeURIComponent(match[1]),
      subTab: match[2] || 'area',
    };
  });

  async function loadDaemonData() {
    try {
      connectionStore.set('connecting');
      const [tablets, settings, vmulti] = await Promise.all([
        fetchTablets(),
        fetchSettings(),
        fetchVMultiStatus(),
      ]);
      tabletsStore.set(tablets);
      settingsStore.set(settings);
      vmultiStore.set(vmulti);
      connectionStore.set('connected');
    } catch {
      connectionStore.set('disconnected');
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
  {#if tabletRoute}
    <TabletDetail tabletName={tabletRoute.tabletName} subTab={tabletRoute.subTab} />
  {:else if hash === '#/tablets'}
    <Profiles />
  {:else if hash === '#/presets'}
    <Presets />
  {:else if hash === '#/console'}
    <Console />
  {:else if hash === '#/about'}
    <About />
  {:else}
    <Dashboard />
  {/if}
</Shell>
