"""Compare the same captured production context; only replace three rule blocks.
Offline token counts are o200k estimates, not Gemini usage. No credentials read here.
"""
import argparse
import json
import re
from pathlib import Path
import tiktoken

ROOT = Path(__file__).resolve().parents[2]


def literal(path, pattern):
    text = (ROOT / path).read_text(encoding="utf-8-sig")
    match = re.search(pattern + r'@"((?:""|[^"])*)"', text)
    if not match:
        raise ValueError(f"C# string not found: {path}")
    return match[1].replace('""', '"').strip()


def compact(prompt, ko):
    region = literal("dev/Source/MapGen/TextRegionPrompt.cs", r'Rules\(bool korean\)\s*=>\s*')
    road = literal("dev/Source/MapGen/RoadPrompt.cs", r'Rules\(bool korean\)\s*=>\s*')
    rec = literal("dev/Source/LLM/RecommendationPlan.cs", r'public const string Rules\s*=\s*')
    # Exact delimiters intentionally fail if the captured format is different.
    a = prompt.index("텍스트 영역·재료·위치 지정:" if ko else "Text regions, materials and positioned structures:")
    b = prompt.index("Active fill materials (loaded natural permanent terrain only):", a)
    prompt = prompt[:a] + region + "\n\n" + prompt[b:]
    a = prompt.index("정착 맵의 도로:" if ko else "Roads inside the settlement map:")
    b = prompt.index("Recommendations and selectable alternatives:", a)
    return prompt[:a] + road + "\n" + rec + "\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    enc = tiktoken.get_encoding("o200k_base")
    rows = []
    for lang, folder in (("ko", "baseline-runtime"), ("en", "baseline-runtime-en")):
        for kind in ("system", "edit"):
            name = "production-system-prompt.txt" if kind == "system" else "production-edit-prompt-template.txt"
            before = (args.output / folder / name).read_text(encoding="utf-8-sig")
            after = compact(before, lang == "ko")
            # Detector control: malformed capture must fail, not yield a zero reduction.
            try:
                compact("invalid capture", lang == "ko")
                raise AssertionError("Missing-section detector did not fail")
            except ValueError:
                pass
            for variant, text in (("before", before), ("after", after)):
                (args.output / f"{lang}-{kind}-{variant}.txt").write_text(text, encoding="utf-8")
            old, new = len(enc.encode(before)), len(enc.encode(after))
            rows.append(dict(language=lang, kind=kind, before=old, after=new,
                             saved=old-new, reduction_percent=round((old-new)/old*100, 2)))
    report = dict(tokenizer="o200k_base (proxy, not Gemini billing)",
                  method="Identical fresh captured tile/catalog/current-state context, three rule blocks replaced",
                  detector_negative_control=True, rows=rows)
    (args.output / "offline-token-comparison.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
