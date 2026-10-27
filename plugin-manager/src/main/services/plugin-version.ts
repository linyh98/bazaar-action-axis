import { promises as fs } from 'node:fs';
import { compareVersions } from './update-core';

export const ACTION_AXIS_PLUGIN_GUID = 'com.bazaarsim.actionaxis';
const ATTRIBUTE_PROLOG = Buffer.from([0x01, 0x00]);
const SEMVER = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$/;

interface SerializedString {
  value: string;
  next: number;
}

/** ECMA-335 SerString: a compressed unsigned byte length followed by UTF-8. */
function readSerializedString(bytes: Buffer, offset: number): SerializedString | null {
  if (offset >= bytes.length) return null;
  const first = bytes[offset];
  let size: number;
  let prefix: number;
  if ((first & 0x80) === 0) {
    size = first;
    prefix = 1;
  } else if ((first & 0xc0) === 0x80) {
    if (offset + 2 > bytes.length) return null;
    size = ((first & 0x3f) << 8) | bytes[offset + 1];
    prefix = 2;
  } else if ((first & 0xe0) === 0xc0) {
    if (offset + 4 > bytes.length) return null;
    size = (first & 0x1f) * 0x1000000 + bytes[offset + 1] * 0x10000
      + bytes[offset + 2] * 0x100 + bytes[offset + 3];
    prefix = 4;
  } else {
    // 0xff is a null SerString; it cannot identify this plugin.
    return null;
  }
  const start = offset + prefix;
  const next = start + size;
  if (next > bytes.length) return null;
  return { value: bytes.toString('utf8', start, next), next };
}

/** Read the BepInPlugin GUID/name/version attribute, not AssemblyVersion or UI text. */
export function extractActionAxisVersion(bytes: Buffer): string | null {
  let offset = bytes.indexOf(ATTRIBUTE_PROLOG);
  while (offset !== -1) {
    const guid = readSerializedString(bytes, offset + ATTRIBUTE_PROLOG.length);
    if (guid?.value === ACTION_AXIS_PLUGIN_GUID) {
      const name = readSerializedString(bytes, guid.next);
      const version = name?.value === 'Bazaar Action Axis'
        ? readSerializedString(bytes, name.next) : null;
      // These plugin attributes have no named arguments (two trailing zero bytes).
      if (version && SEMVER.test(version.value) && version.next + 2 <= bytes.length
        && bytes.readUInt16LE(version.next) === 0) {
        return version.value;
      }
    }
    offset = bytes.indexOf(ATTRIBUTE_PROLOG, offset + 1);
  }
  return null;
}

export async function readActionAxisVersion(filePath: string): Promise<string | null> {
  try {
    return extractActionAxisVersion(await fs.readFile(filePath));
  } catch {
    return null;
  }
}

/** Only builtin deployment uses this guard; explicit ZIP rollback stays available. */
export async function assertBuiltinActionAxisUpgrade(source: string, installed: string): Promise<void> {
  const sourceVersion = await readActionAxisVersion(source);
  if (!sourceVersion) {
    throw new Error('无法读取内置行动轴 DLL 的 BepInPlugin 版本，已取消部署，请更新管理器内置插件。');
  }
  try {
    await fs.access(installed);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return;
    throw error;
  }
  const installedVersion = await readActionAxisVersion(installed);
  if (!installedVersion) {
    throw new Error(`内置行动轴版本为 ${sourceVersion}，但无法读取已安装 DLL 的版本，已取消覆盖。`);
  }
  if (compareVersions(sourceVersion, installedVersion) < 0) {
    throw new Error(`内置行动轴 ${sourceVersion} 低于已安装的 ${installedVersion}，已阻止旧版覆盖，请更新管理器内置插件。`);
  }
}
