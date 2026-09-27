"""Collect the files to upload into upload-v6/ and write review.html, a local page for checking the Workshop page before publishing.

Usage (run from docs/workshop/2026-09-27):  py build_review.py
Open review.html in a browser. It uses relative paths only, so it works from disk or from a local web server.
"""
import hashlib
import html
import os
import re
import shutil
import json

KIT = os.path.dirname(os.path.abspath(__file__))
UPLOAD_ROOT = 'upload-v6'

# (group folder, file name, source, what it is)
UPLOADS = [
    ("1-imgur", "card-01-describe.png", "refresh-v6/out/describe.png", "CARD_01_URL"),
    ("1-imgur", "card-02-editing.png", "refresh-v6/out/editing.png", "CARD_02_URL"),
    ("1-imgur", "card-03-ideas.png", "refresh-v6/out/ideas.png", "CARD_03_URL"),
    ("1-imgur", "card-04-roads.png", "refresh-v6/out/roads.png", "CARD_04_URL"),
    ("2-cover", "Preview.png", "cover/words-to-maps-v5/out/cover.png", "accepted cover v5"),
    ("3-steam-gallery", "gallery-01-recommendations.png", "captures/showcase-02/12-recommend.png", "추천 후보와 선택·재추천 UI"),
    ("3-steam-gallery", "gallery-02-preferences.png", "cards/assets/c3-guide.png", "취향 문답: 공간·방어·경관 선택"),
    ("3-steam-gallery", "gallery-03-editing.png", "captures/showcase-02/13-refine.png", "선택 전 후보 수정 대화"),
    ("3-steam-gallery", "gallery-04-road-bridge.jpg", "refresh-v6/gallery-04-road-bridge.jpg", "실제 생성 맵의 흙길과 나무 다리 · 2 MB 미만"),
]
VIDEO = "video/animatic-draft.mp4"
ALLOWED_TAGS = {"h1", "h2", "h3", "b", "i", "u", "url", "img", "list", "olist", "*"}


def kit(path):
    return os.path.join(KIT, *path.split("/"))


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def collect_uploads():
    card_paths = {}
    for group, name, source, what in UPLOADS:
        target = f"{UPLOAD_ROOT}/{group}/{name}"
        os.makedirs(os.path.dirname(kit(target)), exist_ok=True)
        shutil.copyfile(kit(source), kit(target))
        if sha(kit(source)) != sha(kit(target)):
            raise SystemExit(f"copy differs from source: {target}")
        if what.startswith("CARD_"):
            card_paths[what] = target
        if group == "3-steam-gallery" and os.path.getsize(kit(target)) >= 2_000_000:
            raise SystemExit(f"gallery image exceeds upload limit: {target}")
    for card in json.load(open(kit('image-hosting.json'), encoding='utf-8'))['cards']:
        if card_paths[card['slot']] != card['file'] or sha(kit(card['file'])) != card['localSha256']:
            raise SystemExit(f"hosted card no longer matches local source: {card['slot']}")
        card_paths[card['directUrl']] = card['file']
    return card_paths


def inline(text, card_paths):
    text = html.escape(text, quote=False)
    text = re.sub(r"\[b\](.*?)\[/b\]", r"<strong>\1</strong>", text)
    text = re.sub(r"\[i\](.*?)\[/i\]", r"<em>\1</em>", text)
    text = re.sub(r"\[u\](.*?)\[/u\]", r"<u>\1</u>", text)
    text = re.sub(r"\[url=([^\]]+)\](.*?)\[/url\]", r'<a href="\1" target="_blank" rel="noopener">\2</a>', text)

    def image(match):
        key = match.group(1).strip()
        if key in card_paths:
            return f'<img class="card" src="{card_paths[key]}" alt="card"><span class="slot">{key}</span>'
        if key.startswith("https://"):
            return f'<img class="card" src="{key}" alt="card"><span class="slot">{key}</span>'
        if key not in card_paths:
            raise SystemExit(f"image without a local file: {key}")
        return f'<img class="card" src="{card_paths[key]}" alt="{key}"><span class="slot">{key}</span>'

    return re.sub(r"\[img\](.*?)\[/img\]", image, text)


def render_description(path, card_paths):
    raw = open(kit(path), encoding="utf-8").read()
    unknown = {t for t in re.findall(r"\[/?([a-z0-9*]+)", raw) if t not in ALLOWED_TAGS}
    if unknown:
        raise SystemExit(f"{path}: markup tags this preview does not render: {sorted(unknown)}")
    out, open_list = [], None
    for line in raw.splitlines():
        s = line.strip()
        if s in ("[list]", "[olist]"):
            open_list = "ul" if s == "[list]" else "ol"
            out.append(f"<{open_list}>")
        elif s in ("[/list]", "[/olist]"):
            out.append(f"</{open_list}>")
            open_list = None
        elif s.startswith("[*]"):
            out.append(f"<li>{inline(s[3:].strip(), card_paths)}</li>")
        elif not s:
            if not open_list:
                out.append('<div class="gap"></div>')
        else:
            m = re.fullmatch(r"\[(h[123])\](.*)\[/\1\]", s)
            if m:
                out.append(f"<{m.group(1)}>{inline(m.group(2), card_paths)}</{m.group(1)}>")
            elif s.startswith("[img]"):
                out.append(f'<div class="figure">{inline(s, card_paths)}</div>')
            else:
                out.append(f"<p>{inline(s, card_paths)}</p>")
    if open_list:
        raise SystemExit(f"{path}: a list is not closed")
    return "\n".join(out)


# The current review layout is refresh-v6/review-template.html.


def item(target, what):
    name = target.rsplit("/", 1)[-1]
    return (f'<div class="item"><a href="{target}" target="_blank"><img src="{target}" alt="{html.escape(name)}"></a>'
            f'<div class="name">{html.escape(name)}</div><div class="what">{html.escape(what)}</div></div>')


def main():
    card_paths = collect_uploads()
    imgur = [(f"{UPLOAD_ROOT}/{g}/{n}", w) for g, n, s, w in UPLOADS if g == "1-imgur"]
    gallery = [(f"{UPLOAD_ROOT}/{g}/{n}", w) for g, n, s, w in UPLOADS if g == "3-steam-gallery"]
    cover = f"{UPLOAD_ROOT}/2-cover/Preview.png"
    page = open(kit('refresh-v6/review-template.html'), encoding='utf-8').read()
    for key, value in {
        "__IMGUR_DIR__": UPLOAD_ROOT + "/1-imgur",
        "__IMGUR_ITEMS__": "".join(item(t, "소개글의 " + w + " 자리") for t, w in imgur),
        "__DESC_FILES__": "description-en.txt  ·  description-ko.txt",
        "__DESC_EN__": render_description("description-en.txt", card_paths),
        "__DESC_KO__": render_description("description-ko.txt", card_paths),
        "__GALLERY_DIR__": UPLOAD_ROOT + "/3-steam-gallery",
        "__GALLERY_ITEMS__": "".join(item(t, w) for t, w in gallery),
        "__COVER_PATH__": cover,
        "__COVER_SRC__": cover,
    }.items():
        if key not in page:
            raise SystemExit(f"template slot missing: {key}")
        page = page.replace(key, value)
    open(kit("review.html"), "w", encoding="utf-8", newline="\n").write(page)
    manifest = {'workshopId': '3685385453', 'published': False, 'imageHostingComplete': True,
        'imageHostingEvidence': '../image-hosting.json',
        'files': [{'file': f'{UPLOAD_ROOT}/{g}/{n}', 'source': s, 'sha256': sha(kit(s)), 'use': w} for g,n,s,w in UPLOADS]}
    open(kit(UPLOAD_ROOT + '/manifest.json'), 'w', encoding='utf-8').write(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print(f"review.html written; {len(UPLOADS)} verified files in {UPLOAD_ROOT}/; public upload pending")


if __name__ == "__main__":
    main()
