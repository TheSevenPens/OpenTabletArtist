import type { DaemonEvent } from '../types/events';
import { connectionStore } from '../stores/connection.svelte';
import { tabletsStore } from '../stores/tablets.svelte';

let ws: WebSocket | null = null;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
let reconnectDelay = 1000;

export function connectWebSocket() {
  if (ws?.readyState === WebSocket.OPEN) return;

  connectionStore.set('connecting');
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  ws = new WebSocket(`${protocol}//${location.host}/ws`);

  ws.onopen = () => {
    connectionStore.set('connected');
    reconnectDelay = 1000;
  };

  ws.onclose = () => {
    connectionStore.set('disconnected');
    scheduleReconnect();
  };

  ws.onerror = () => {
    ws?.close();
  };

  ws.onmessage = (ev) => {
    try {
      const event: DaemonEvent = JSON.parse(ev.data);
      handleEvent(event);
    } catch {
      // ignore malformed messages
    }
  };
}

function handleEvent(event: DaemonEvent) {
  switch (event.type) {
    case 'tabletsChanged':
      tabletsStore.set(event.data);
      break;
    case 'resynchronize':
      // trigger a settings re-fetch from the caller
      break;
  }
}

function scheduleReconnect() {
  if (reconnectTimer) clearTimeout(reconnectTimer);
  reconnectTimer = setTimeout(() => {
    reconnectDelay = Math.min(reconnectDelay * 1.5, 10000);
    connectWebSocket();
  }, reconnectDelay);
}

export function disconnectWebSocket() {
  if (reconnectTimer) clearTimeout(reconnectTimer);
  ws?.close();
  ws = null;
}
