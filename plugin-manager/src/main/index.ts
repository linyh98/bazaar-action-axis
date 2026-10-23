import { app, BrowserWindow, shell } from 'electron';
import path from 'node:path';
import { buildBootstrap, registerIpc } from './ipc';
import { describeError, log } from './logger';
import { removePath } from './services/fsutil';

const gotLock = app.requestSingleInstanceLock();

if (!gotLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    const window = BrowserWindow.getAllWindows()[0];
    if (window) {
      if (window.isMinimized()) window.restore();
      window.focus();
    }
  });

  app.whenReady().then(async () => {
    try {
      await removePath(path.join(app.getPath('userData'), 'tmp'));
    } catch {
      // 临时目录清理失败不影响启动
    }

    registerIpc();
    createWindow();
    await log('[app] 管理器已启动');

    // 预热一次检测，让首屏状态更快可用
    buildBootstrap().catch((error) => log(`[app] 启动检测失败：${describeError(error)}`));

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  });

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit();
  });
}

function createWindow(): void {
  const window = new BrowserWindow({
    width: 1180,
    height: 800,
    minWidth: 960,
    minHeight: 640,
    show: false,
    backgroundColor: '#14100d',
    title: 'Bazaar 插件管理器',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, '../preload/index.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });

  window.on('ready-to-show', () => window.show());

  window.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:\/\//i.test(url)) {
      void shell.openExternal(url);
    }
    return { action: 'deny' };
  });

  const devUrl = process.env['ELECTRON_RENDERER_URL'];
  if (devUrl) {
    void window.loadURL(devUrl);
  } else {
    void window.loadFile(path.join(__dirname, '../renderer/index.html'));
  }
}
