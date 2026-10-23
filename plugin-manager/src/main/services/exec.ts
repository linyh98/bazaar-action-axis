import { execFile } from 'node:child_process';

/** 运行外部命令并返回 stdout；失败时抛出带 stderr 的错误 */
export function runCommand(file: string, args: string[], timeoutMs = 15000): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile(
      file,
      args,
      { timeout: timeoutMs, windowsHide: true, maxBuffer: 8 * 1024 * 1024 },
      (error, stdout, stderr) => {
        if (error) {
          const detail = stderr ? `\n${stderr}` : '';
          reject(new Error(`${error.message}${detail}`));
          return;
        }
        resolve(stdout);
      }
    );
  });
}
