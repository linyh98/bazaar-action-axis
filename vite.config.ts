import { spawn } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { mapBpp, mapOcr, type CardLite } from './tools/live_cards_map';

function runOcr(root: string): Promise<void> {
  return new Promise((resolvePromise, reject) => {
    const child = spawn(
      'python',
      ['tools/read_current_cards.py', '--out-dir', 'captures/live_api'],
      { cwd: root, windowsHide: true },
    );
    let stderr = '';
    child.stderr.on('data', (chunk) => {
      stderr += chunk.toString();
    });
    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) resolvePromise();
      else reject(new Error(`OCR exited ${code}: ${stderr.slice(-500)}`));
    });
  });
}

function liveCardsApi() {
  return {
    name: 'bazaar-live-cards-api',
    configureServer(server: any) {
      server.middlewares.use('/api/live-cards', async (_req: any, res: any) => {
        try {
          const root = process.cwd();
          const cards = JSON.parse(
            readFileSync(resolve(root, 'src/data/cards.json'), 'utf-8'),
          ) as CardLite[];
          const bppPath = resolve(root, 'captures/live_cards_bpp.json');
          let result = null;

          if (
            existsSync(bppPath) &&
            Date.now() - statSync(bppPath).mtimeMs < 60000
          ) {
            const dump = JSON.parse(readFileSync(bppPath, 'utf-8'));
            result = mapBpp(dump, cards, 'shop');
          }

          const ocrPath = resolve(root, 'captures/live_api/live_cards.json');
          if (
            !result &&
            existsSync(ocrPath) &&
            Date.now() - statSync(ocrPath).mtimeMs < 60000
          ) {
            result = mapOcr(
              JSON.parse(readFileSync(ocrPath, 'utf-8')),
              cards,
            );
          }

          if (!result) {
            await runOcr(root);
            if (!existsSync(ocrPath)) {
              throw new Error('OCR did not produce live_cards.json');
            }
            const ocr = JSON.parse(readFileSync(ocrPath, 'utf-8'));
            result = mapOcr(ocr, cards);
          }

          res.statusCode = 200;
          res.setHeader('Content-Type', 'application/json; charset=utf-8');
          res.end(JSON.stringify(result));
        } catch (error) {
          res.statusCode = 500;
          res.setHeader('Content-Type', 'application/json; charset=utf-8');
          res.end(
            JSON.stringify({
              error: error instanceof Error ? error.message : String(error),
            }),
          );
        }
      });
    },
  };
}

export default defineConfig({
  plugins: [react(), liveCardsApi()],
  test: {
    environment: 'jsdom',
    globals: true,
    exclude: ['**/node_modules/**', '**/dist/**', 'vendor/**', 'plugin-manager/**'],
  },
});
