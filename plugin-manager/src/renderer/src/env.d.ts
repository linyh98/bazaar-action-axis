import type { ManagerApi } from '@shared/types';

declare global {
  interface Window {
    bazaarManager: ManagerApi;
  }
}

export {};
