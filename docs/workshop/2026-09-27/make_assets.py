"""Rebuild the Workshop card and cover images from real game captures and record where each one came from.

Usage (run from docs/workshop/2026-09-27):
  py make_assets.py assets   crop or upscale captures into cards/assets and cover/assets, write cards/assets/SOURCES.json
  py make_assets.py sheets   after build_images.mjs: list-size covers, cover/out/compare.jpg, cards/out/all-cards.jpg
"""
import hashlib
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

KIT = os.path.dirname(os.path.abspath(__file__))
LIVE = "captures/showcase-02"  # real model calls on the fixed build
REPLAY = "captures/showcase-03-replay"  # replies recorded from real calls, replayed on the fixed build

# asset -> (source, crop box in the 1920x1080 screenshot, or "x2" = nearest-neighbour upscale of a 250x250 preview)
ASSETS = {
    "cards/assets/c1-ui-open.png": (REPLAY + "/01-open.png", (60, 30, 1860, 1030)),
    "cards/assets/c1-chat.png": (REPLAY + "/03-ring.png", (115, 448, 1380, 712)),
    "cards/assets/c1-before.png": (REPLAY + "/01-open-preview.png", "x2"),
    "cards/assets/c1-after.png": (REPLAY + "/03-ring-preview.png", "x2"),
    "cards/assets/c2-step1.png": (REPLAY + "/03-ring-preview.png", "x2"),
    "cards/assets/c2-step2.png": (REPLAY + "/04-island-preview.png", "x2"),
    "cards/assets/c2-step3.png": (REPLAY + "/05-island-move-preview.png", "x2"),
    "cards/assets/c2-coast-before.png": (LIVE + "/14-coast-before-preview.png", "x2"),
    "cards/assets/c2-coast-after.png": (LIVE + "/14-coast-preview.png", "x2"),
    "cards/assets/c3-candidates.png": (LIVE + "/12-recommend.png", (105, 535, 1410, 900)),
    "cards/assets/c3-opt2-before.png": (LIVE + "/12-recommend-option-2.png", "x2"),
    "cards/assets/c3-opt2-after.png": (LIVE + "/13-refine-option-2.png", "x2"),
    "cards/assets/c3-guide.png": (LIVE + "/02-guide.png", (262, 55, 1250, 665)),
    "cards/assets/c4-map.png": (REPLAY + "/14-map-1.png", (0, 50, 1690, 1015)),
    "cover/assets/a-map.png": (REPLAY + "/03-ring-preview.png", "x2"),
    "cover/assets/b-1.png": (REPLAY + "/01-open-preview.png", "x2"),
    "cover/assets/b-2.png": (REPLAY + "/03-ring-preview.png", "x2"),
    "cover/assets/b-3.png": (REPLAY + "/05-island-move-preview.png", "x2"),
    "cover/assets/c-bg.png": (REPLAY + "/14-map-1.png", (180, 65, 1620, 875)),
}
COVERS = [("coverA", "A  request -> result"), ("coverB", "B  tile -> first request -> follow-up"), ("coverC", "C  real generated map")]
CARDS = ["describe", "editing", "ideas", "realmap"]
BACKGROUND = (13, 17, 23)


def kit(path):
    return os.path.join(KIT, *path.split("/"))


def build_assets():
    sources = {}
    for asset, (source, op) in ASSETS.items():
        data = open(kit(source), "rb").read()
        image = Image.open(kit(source)).convert("RGB")
        entry = {"source": source, "sourceSha256_16": hashlib.sha256(data).hexdigest()[:16]}
        if op == "x2":
            if image.size != (250, 250):
                raise SystemExit(f"{source}: expected a 250x250 preview, got {image.size}")
            image = image.resize((500, 500), Image.NEAREST)
            entry["scale"] = "nearest x2"
        else:
            if image.size != (1920, 1080):
                raise SystemExit(f"{source}: expected a 1920x1080 capture, got {image.size}")
            image = image.crop(op)
            entry["crop"] = list(op)
        entry["size"] = list(image.size)
        image.save(kit(asset))
        sources[asset] = entry
    json.dump(sources, open(kit("cards/assets/SOURCES.json"), "w", encoding="utf-8"), indent=1)
    print(f"{len(sources)} assets written")


def font(size):
    for name in ("arialbd.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def build_sheets():
    rows = []
    for cover_id, label in COVERS:
        full = Image.open(kit(f"cover/out/{cover_id}.png")).convert("RGB")
        if full.size != (960, 540):
            raise SystemExit(f"{cover_id}: expected 960x540, got {full.size}")
        small = full.resize((204, 115), Image.LANCZOS)
        small.save(kit(f"cover/out/{cover_id}-list204.png"))
        rows.append((label, full, small.resize((408, 230), Image.NEAREST)))
    sheet = Image.new("RGB", (960 + 30 + 408, 620 * len(rows)), BACKGROUND)
    draw = ImageDraw.Draw(sheet)
    for i, (label, full, small) in enumerate(rows):
        top = i * 620
        draw.text((0, top + 6), label, fill=(240, 240, 240), font=font(22))
        sheet.paste(full, (0, top + 40))
        sheet.paste(small, (990, top + 40))
        draw.text((990, top + 280), "Steam list size (204x115), shown 2x", fill=(170, 176, 184), font=font(15))
    sheet.save(kit("cover/out/compare.jpg"), quality=88)
    cards = [Image.open(kit(f"cards/out/{name}.png")).convert("RGB") for name in CARDS]
    wall = Image.new("RGB", (sum(c.width for c in cards) + 20 * (len(cards) - 1), max(c.height for c in cards)), BACKGROUND)
    x = 0
    for card in cards:
        wall.paste(card, (x, 0))
        x += card.width + 20
    wall.save(kit("cards/out/all-cards.jpg"), quality=88)
    print("sheets written")


if __name__ == "__main__":
    step = sys.argv[1] if len(sys.argv) > 1 else ""
    if step == "assets":
        build_assets()
    elif step == "sheets":
        build_sheets()
    else:
        raise SystemExit(__doc__)
