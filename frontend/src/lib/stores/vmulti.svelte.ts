export interface VMultiStatus {
  installed: boolean;
  functional: boolean;
  message: string;
}

export const vmultiStore = createVMultiStore();

function createVMultiStore() {
  let status = $state<VMultiStatus>({ installed: false, functional: false, message: 'Checking...' });

  return {
    get status() { return status; },
    get isInstalled() { return status.installed; },
    get isFunctional() { return status.functional; },
    get message() { return status.message; },
    set(s: VMultiStatus) { status = s; }
  };
}
