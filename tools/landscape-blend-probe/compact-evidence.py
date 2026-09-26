"""Losslessly replace only this probe's large, newly authored native JSON files."""
import gzip
import hashlib
import json
from pathlib import Path

root=(Path(__file__).resolve().parents[2]/"docs/analysis/2026-09-23-landscape-blending").resolve()
manifest=root/"native-compression.json"
if manifest.exists(): raise SystemExit("Compression receipt already exists; do not rewrite evidence")
entries=[]
for directory in sorted(root.glob("native-blend-*")):
    if not directory.is_dir(): continue
    for candidate in sorted(directory.glob("*.json")):
        source=candidate.resolve(strict=True)
        if not source.is_relative_to(root) or source.parent!=directory.resolve(): raise SystemExit("Evidence path escaped owned root")
        if source.stat().st_size<100*1024: continue
        target=source.with_suffix(source.suffix+".gz")
        if target.exists(): raise SystemExit("Will not overwrite existing compressed data")
        data=source.read_bytes();digest=hashlib.sha256(data).hexdigest()
        with target.open("xb") as out:
            with gzip.GzipFile(filename="",mode="wb",fileobj=out,mtime=0) as compressed: compressed.write(data)
        with gzip.open(target,"rb") as stored: restored=stored.read()
        if restored!=data or hashlib.sha256(restored).hexdigest()!=digest: raise SystemExit("Roundtrip verification failed; original retained")
        # Exact resolved owned source checked above; only remove our identical uncompressed copy.
        source.unlink()
        entries.append(dict(path=str(target.relative_to(root)),originalBytes=len(data),compressedBytes=target.stat().st_size,originalSha256=digest,roundtripVerified=True))
result=dict(files=len(entries),originalBytes=sum(e["originalBytes"] for e in entries),compressedBytes=sum(e["compressedBytes"] for e in entries),entries=entries)
manifest.write_text(json.dumps(result,indent=2),encoding="utf-8")
print(json.dumps({k:v for k,v in result.items() if k!="entries"}))
