"""Summarize recorded usage; no API calls. Prices are explicit assumptions."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def results(folder):
    return json.loads((ROOT / folder / "result.json").read_text(encoding="utf-8-sig"))["results"]


initial = results("provider-pairs")
edits = results("provider-edits-final")
recommend = results("provider-recommendations-final")
pairs = []
for after in edits + [r for r in recommend if r["id"].endswith("-after")]:
    before_id = after["id"].removesuffix("-after") + "-before"
    pool = recommend if after["id"].startswith("recommend-") else initial
    before = next(r for r in pool if r["id"] == before_id)
    lang = "en" if any(x in after["id"] for x in ("-en", "candidate", "inland-coast")) else "ko"
    pairs.append(dict(case=after["id"].removesuffix("-after"), language=lang, before=before, after=after))

def usage(rows):
    data = {k: sum(r[k] for r in rows) for k in ("inputTokens", "outputTokens", "thinkingTokens")}
    data["estimatedUSD"] = (data["inputTokens"] * .75 + (data["outputTokens"] + data["thinkingTokens"]) * 3.75) / 1_000_000
    return data

summary = {}
for lang in ("ko", "en", "all"):
    group = [p for p in pairs if lang == "all" or p["language"] == lang]
    before, after = usage([p["before"] for p in group]), usage([p["after"] for p in group])
    summary[lang] = dict(cases=len(group), before=before, after=after,
                        inputSavedPercent=round((1-after["inputTokens"]/before["inputTokens"])*100, 2),
                        estimatedCostSavedPercent=round((1-after["estimatedUSD"]/before["estimatedUSD"])*100, 2))

# Validate that the measured compact instructions match the final real runtime DLL.
runtime = (ROOT / "final-runtime/production-edit-prompt-template.txt").read_text(encoding="utf-8-sig")
prepared = (ROOT / "final-prompts/ko-edit-after.txt").read_text(encoding="utf-8")
markers = [("Text regions, materials and structures:", "Active fill materials"),
           ("Local roads and follow-ups:", "Recommendations:"), ("Recommendations:", None)]
for a, b in markers:
    def section(s):
        start = s.index(a)
        end = s.index(b, start + len(a)) if b else len(s)
        return re.sub(r"\s+", " ", s[start:end]).strip()
    assert section(runtime) == section(prepared), f"Runtime rule mismatch: {a}"

report = dict(model="gemini-3.8-flash", measured="Provider usageMetadata, not local token estimates",
              rates=dict(inputUSDPerMillion=.75, outputAndThinkingUSDPerMillion=3.75,
                         source="https://ai.google.dev/gemini-api/docs/pricing", checked="2026-09-27",
                         note="Standard rates through 2026-12-31; excludes caching, taxes and FX"),
              finalRuleBlocksMatchRuntime=True, paired=summary,
              finalChecks=dict(edits=f"{sum(r['ok'] for r in edits)}/{len(edits)}",
                               recommendations=f"{sum(p['after']['ok'] for p in pairs if p['case'].startswith('recommend-'))}/6"),
              totalLiveCalls=len(initial)+len(edits)+len(recommend),
              totalBenchmarkUsage=usage(initial+edits+recommend), pairs=pairs,
              limitations="Small controlled sample; same histories/states, no repair calls. Native world contexts differ between KO/EN captures but are identical within each pair. Does not prove visual quality, feature compatibility with all mods, or every conversation.")
(ROOT / "summary.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if k != "pairs"}, indent=2))
