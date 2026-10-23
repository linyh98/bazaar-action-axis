"""临时检查工具：查看 bazaarwinner 页面结构。"""
import re
import sys
from pathlib import Path

path = Path(sys.argv[1])
html = path.read_text(encoding="utf-8", errors="replace")
print("LEN", len(html))

print("\n--- next_f occurrences ---")
idx = 0
for i in range(4):
    idx = html.find("self.__next_f.push", idx)
    if idx < 0:
        break
    print(f"[{i}] at {idx}")
    print(html[idx:idx + 500])
    idx += 10

print("\n--- script srcs ---")
for s in re.findall(r'<script[^>]+src="([^"]+)', html):
    print(s)

print("\n--- link/urls containing api ---")
for u in sorted(set(re.findall(r'["\']([^"\']*api[^"\']*)["\']', html, re.I))):
    print(u[:200])
