export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected';

export const connectionStore = createConnectionStore();

function createConnectionStore() {
  let status = $state<ConnectionStatus>('disconnected');

  return {
    get status() { return status; },
    get isConnected() { return status === 'connected'; },
    set(s: ConnectionStatus) { status = s; }
  };
}
