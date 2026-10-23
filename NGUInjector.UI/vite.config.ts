import { svelte, vitePreprocess } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vite';

const svelteOptions = { preprocess: vitePreprocess() };

export default defineConfig({
  cacheDir: process.env.NGUI_TEST_VITE_CACHE,
  plugins: [
    // svelte-check 4.7 loads this config API; vite-plugin-svelte 5 does not expose it itself.
    { name: 'vite-plugin-svelte:config', api: { options: svelteOptions } },
    svelte(svelteOptions)
  ],
  clearScreen: false,
  // Only the frontend is an entry point, not generated Tauri HTML under src-tauri/target.
  optimizeDeps: { entries: ['index.html'] },
  server: {
    strictPort: true,
    port: 1420,
    watch: { ignored: ['**/src-tauri/**'] }
  },
  envPrefix: ['VITE_', 'TAURI_']
});
