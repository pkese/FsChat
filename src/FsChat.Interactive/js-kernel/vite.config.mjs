// vite.config.js
import { defineConfig } from 'vite';

import resolve from '@rollup/plugin-node-resolve';
import commonjs from '@rollup/plugin-commonjs';
import terser from '@rollup/plugin-terser';

export default defineConfig({
  build: {
    rollupOptions: {
      input: 'src/main.js',
      output: {
        dir: 'dist',
        entryFileNames: 'main.js',
        chunkFileNames: '[name].js',
        assetFileNames: '[name].[ext]',
        format: 'iife',
        inlineDynamicImports: true,
      },
      plugins: [
        resolve(), // tells Rollup how to find node_modules
        commonjs(), // converts CommonJS modules to ES6
        terser({
          output: {
            ecma: 6,
            quote_style: 1, // use default quotes (0=double, 1=single, 2=auto, 3=keep original)
          }
        }), // minify the bundle
      ],
      treeshake: {
        moduleSideEffects: false // Ensure no side effects are included
      }
    }
  },
  output: {
    generatedCode: "es2020",
  }
});
