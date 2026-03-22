import type { TabletInfo } from '../types/tablet';

export const tabletsStore = createTabletsStore();

function createTabletsStore() {
  let tablets = $state<TabletInfo[]>([]);

  return {
    get list() { return tablets; },
    get current() { return tablets[0] ?? null; },
    get hasTablet() { return tablets.length > 0; },
    set(t: TabletInfo[]) { tablets = t; }
  };
}
