"""从 bazaarwinner 的 Next.js 数据 chunk 中提取物品图鉴 JSON。

用法:
    python tools/extract_items.py <chunk.js> <out.json>
"""
import json
import sys
from pathlib import Path


def iter_objects(text: str):
    """扫描所有形如 {"id": ... 的对象，按花括号配对提取。"""
    needle = '{"id":"'
    pos = 0
    while True:
        start = text.find(needle, pos)
        if start < 0:
            return
        depth = 0
        in_str = False
        esc = False
        end = None
        for k in range(start, len(text)):
            ch = text[k]
            if in_str:
                if esc:
                    esc = False
                elif ch == "\\":
                    esc = True
                elif ch == '"':
                    in_str = False
            else:
                if ch == '"':
                    in_str = True
                elif ch == "{":
                    depth += 1
                elif ch == "}":
                    depth -= 1
                    if depth == 0:
                        end = k + 1
                        break
        if end is None:
            return
        yield start, text[start:end]
        pos = end


def parse_obj(raw: str):
    for candidate in (raw, raw.replace("\\'", "'")):
        try:
            return json.loads(candidate)
        except json.JSONDecodeError:
            continue
    return None


def main() -> None:
    src = Path(sys.argv[1])
    out = Path(sys.argv[2])
    text = src.read_text(encoding="utf-8", errors="replace")

    items = {}
    failed = 0
    for _, raw in iter_objects(text):
        obj = parse_obj(raw)
        if obj is None:
            failed += 1
            continue
        if not obj.get("id") or "tierData" not in obj:
            continue
        items.setdefault(obj["id"], obj)

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(
        json.dumps(list(items.values()), ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    with_tiers = sum(1 for i in items.values() if i.get("tierData"))
    print(f"提取物品: {len(items)}，含 tierData: {with_tiers}，解析失败: {failed}")
    print(f"输出: {out}")


if __name__ == "__main__":
    main()
