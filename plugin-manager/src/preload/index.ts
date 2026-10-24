import { contextBridge, ipcRenderer } from 'electron';
import type { AppConfig, BuiltinPluginId, ManagerApi } from '../shared/types';

const api: ManagerApi = {
  bootstrap: () => ipcRenderer.invoke('manager:bootstrap'),
  detectGame: () => ipcRenderer.invoke('manager:detect'),
  chooseGamePath: () => ipcRenderer.invoke('manager:chooseGamePath'),
  launchGame: () => ipcRenderer.invoke('manager:launch'),
  installBuiltin: (id: BuiltinPluginId) => ipcRenderer.invoke('manager:installBuiltin', id),
  installFromZip: () => ipcRenderer.invoke('manager:installZip'),
  installFromUrl: (url: string) => ipcRenderer.invoke('manager:installUrl', url),
  uninstallPlugin: (id: string) => ipcRenderer.invoke('manager:uninstallPlugin', id),
  refreshData: () => ipcRenderer.invoke('manager:refreshData'),
  cleanData: (ids: string[]) => ipcRenderer.invoke('manager:cleanData', ids),
  openPath: (target: string) => ipcRenderer.invoke('manager:openPath', target),
  openUserData: () => ipcRenderer.invoke('manager:openUserData'),
  openExternal: (url: string) => ipcRenderer.invoke('manager:openExternal', url),
  updateConfig: (partial: Partial<AppConfig>) => ipcRenderer.invoke('manager:updateConfig', partial),
  copyToClipboard: (text: string) => ipcRenderer.invoke('manager:copy', text),
  checkUpdate: () => ipcRenderer.invoke('manager:checkUpdate'),
  downloadUpdate: () => ipcRenderer.invoke('manager:downloadUpdate'),
  applyUpdate: () => ipcRenderer.invoke('manager:applyUpdate'),
  updateBuiltinFromRelease: () => ipcRenderer.invoke('manager:updateBuiltinFromRelease')
};

contextBridge.exposeInMainWorld('bazaarManager', api);
