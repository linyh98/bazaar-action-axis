#!/usr/bin/env node
// 打包管理器并把 portable exe 与内置插件 DLL 发布为 GitHub Release。
// 用法：node scripts/publish-release.mjs [--skip-build] [--draft] [--dry-run]
// 需要已登录的 gh CLI；走代理时先设置 HTTPS_PROXY。

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(here, '..');
const workspaceRoot = path.resolve(projectRoot, '..');
const args = new Set(process.argv.slice(2));
const dryRun = args.has('--dry-run');
const draft = args.has('--draft');
const skipBuild = args.has('--skip-build');

const pkg = JSON.parse(readFileSync(path.join(projectRoot, 'package.json'), 'utf8'));
const version = pkg.version;
const tag = `v${version}`;
const productName = 'Bazaar Plugin Manager';

const managerAsset = path.join(projectRoot, 'release', `${productName}-${version}-portable.exe`);
const builtinAssets = [
  path.join(workspaceRoot, 'tools', 'actionaxis_plugin', 'build', 'ActionAxisPlugin.dll'),
  path.join(workspaceRoot, 'tools', 'actionaxis_plugin', 'build', 'ActionAxisEngine.dll'),
  path.join(workspaceRoot, 'tools', 'livecards_plugin', 'LiveCardsDump.dll')
];

function run(command, commandArgs, options = {}) {
  const result = spawnSync(command, commandArgs, {
    cwd: projectRoot,
    stdio: 'inherit',
    shell: process.platform === 'win32',
    ...options
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${commandArgs.join(' ')} 失败（exit ${result.status}）`);
  }
}

function gh(argsList) {
  if (dryRun) {
    console.log(`[dry-run] gh ${argsList.join(' ')}`);
    return '';
  }
  const result = spawnSync('gh', argsList, {
    cwd: projectRoot,
    encoding: 'utf8',
    shell: process.platform === 'win32'
  });
  if (result.status !== 0) {
    throw new Error(result.stderr?.trim() || `gh ${argsList.join(' ')} 失败`);
  }
  return result.stdout.trim();
}

if (!skipBuild) {
  console.log(`\n[1/3] 打包 v${version} …`);
  run(process.platform === 'win32' ? 'npm.cmd' : 'npm', ['run', 'package']);
}

console.log('\n[2/3] 检查发布文件 …');
const missing = [managerAsset, ...builtinAssets].filter((file) => !existsSync(file));
if (missing.length > 0) {
  console.error('缺少以下文件，无法发布：');
  for (const file of missing) console.error(`  - ${file}`);
  process.exit(1);
}
for (const file of [managerAsset, ...builtinAssets]) {
  console.log(`  ✓ ${path.relative(projectRoot, file)}`);
}

const notes = [
  `## Bazaar 插件管理器 v${version}`,
  '',
  '- 支持从 GitHub Release 检查并自动更新管理器（退出后替换 portable exe 并重启）',
  '- 支持从同一个 Release 单独更新内置插件 DLL',
  '',
  '### 内置资源版本',
  '',
  ...builtinAssets.map((file) => `- \`${path.basename(file)}\``),
  '',
  '安装方式：下载 `' + path.basename(managerAsset) + '` 直接运行，无需安装。'
].join('\n');

console.log(`\n[3/3] 创建 Release ${tag} …`);
const createArgs = [
  'release',
  'create',
  tag,
  managerAsset,
  ...builtinAssets,
  '--title',
  `Bazaar 插件管理器 v${version}`,
  '--notes',
  notes
];
if (draft) createArgs.push('--draft');
const url = gh(createArgs);
if (url) console.log(`\n完成：${url}`);
console.log('提示：管理器读取的是 latest Release，草稿不会出现在检查结果里。');
