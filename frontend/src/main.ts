import { mount } from 'svelte'
import './app.css'
import App from './App.svelte'

try {
  const app = mount(App, {
    target: document.getElementById('app')!,
  })
  console.log('App mounted successfully', app)
} catch (e) {
  console.error('Mount failed:', e)
}
