import { runCommand } from './exec';

/** 检查指定 exe 是否正在运行（仅 Windows） */
export async function isProcessRunning(exeName: string): Promise<boolean> {
  if (process.platform !== 'win32') return false;
  try {
    const output = await runCommand('tasklist', ['/FI', `IMAGENAME eq ${exeName}`, '/NH']);
    return output.toLowerCase().includes(exeName.toLowerCase());
  } catch {
    return false;
  }
}
