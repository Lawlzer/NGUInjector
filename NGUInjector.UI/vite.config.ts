import { svelte, vitePreprocess } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vite';

const svelteOptions = { preprocess: vitePreprocess() };

export default defineConfig({
  plugins: [
    // svelte-check 4.7 loads this config API; vite-plugin-svelte 5 does not expose it itself.
    { name: 'vite-plugin-svelte:config', api: { options: svelteOptions } },
    svelte(svelteOptions)
  ],
  clearScreen: false,
  server: {
    strictPort: true,
    port: 1420
  },
  envPrefix: ['VITE_', 'TAURI_']
});
