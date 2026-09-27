"""Collect the files to upload into upload/ and write review.html, a local page for checking the Workshop page before publishing.

Usage (run from docs/workshop/2026-09-27):  py build_review.py
Open review.html in a browser. It uses relative paths only, so it works from disk or from a local web server.
"""
import hashlib
import html
import os
import re
import shutil

KIT = os.path.dirname(os.path.abspath(__file__))

# (group folder, file name, source, what it is)
UPLOADS = [
    ("1-imgur", "card-01-describe.png", "cards/out/describe.png", "CARD_01_URL"),
    ("1-imgur", "card-02-editing.png", "cards/out/editing.png", "CARD_02_URL"),
    ("1-imgur", "card-03-ideas.png", "cards/out/ideas.png", "CARD_03_URL"),
    ("1-imgur", "card-04-play.png", "cards/out/realmap.png", "CARD_04_URL"),
    ("2-cover", "Preview.png", "cover/out/coverA.png", "cover A"),
    ("3-steam-gallery", "gallery-1-chat.png", "captures/showcase-03-replay/03-ring.png", "The chat and Map Preview after the ring request"),
    ("3-steam-gallery", "gallery-2-ideas.png", "captures/showcase-02/12-recommend.png", "Three suggested maps"),
    ("3-steam-gallery", "gallery-3-road-bridge.png", "captures/showcase-04-road-replay/14-map-2.png", "A dirt road crossing the river on a wooden bridge"),
    ("3-steam-gallery", "gallery-4-map.png", "captures/showcase-03-replay/14-map-1.png", "The real map from the same chat"),
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
        target = f"upload/{group}/{name}"
        os.makedirs(os.path.dirname(kit(target)), exist_ok=True)
        shutil.copyfile(kit(source), kit(target))
        if sha(kit(source)) != sha(kit(target)):
            raise SystemExit(f"copy differs from source: {target}")
        if what.startswith("CARD_"):
            card_paths[what] = target
    return card_paths


def inline(text, card_paths):
    text = html.escape(text, quote=False)
    text = re.sub(r"\[b\](.*?)\[/b\]", r"<strong>\1</strong>", text)
    text = re.sub(r"\[i\](.*?)\[/i\]", r"<em>\1</em>", text)
    text = re.sub(r"\[u\](.*?)\[/u\]", r"<u>\1</u>", text)
    text = re.sub(r"\[url=([^\]]+)\](.*?)\[/url\]", r'<a href="\1" target="_blank" rel="noopener">\2</a>', text)

    def image(match):
        key = match.group(1).strip()
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


PAGE = """<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>MapGen AI 창작마당 검토</title>
<style>
:root{--bg:#0f1720;--panel:#16212d;--panel2:#1c2a38;--ink:#e8ecef;--muted:#9fb0bf;--accent:#6cb6ff;--line:#2a3a4a;--warn:#f0b35a}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 "Segoe UI","Malgun Gothic",sans-serif}
main{max-width:1060px;margin:0 auto;padding:24px 16px 64px}
h1.page{font-size:26px;margin:0 0 4px}
.sub{color:var(--muted);margin:0 0 24px}
section{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:18px 18px 8px;margin:0 0 20px}
section>h2{margin:0 0 4px;font-size:19px}
.when{color:var(--muted);margin:0 0 14px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:14px;margin-bottom:12px}
.item{background:var(--panel2);border:1px solid var(--line);border-radius:8px;padding:10px}
.item img{width:100%;height:160px;object-fit:cover;object-position:top;border-radius:4px;display:block;background:#0b1118}
.item .name{font-weight:600;margin-top:8px;word-break:break-all}
.item .what{color:var(--muted);font-size:13px}
.path{font:12px/1.4 Consolas,monospace;color:var(--muted);word-break:break-all;background:#0b1118;border-radius:4px;padding:6px 8px;margin:6px 0 12px}
.cover-row{display:flex;flex-wrap:wrap;gap:16px;align-items:flex-start;margin-bottom:12px}
.cover-row img.full{width:min(480px,100%);border-radius:6px;border:1px solid var(--line)}
.cover-row .small{color:var(--muted);font-size:13px}
.cover-row .small img{display:block;width:204px;height:115px;margin:0 0 6px;border:1px solid var(--line)}
video{width:100%;border-radius:8px;border:1px solid var(--line);background:#000;margin-bottom:12px}
.tabs{display:flex;gap:8px;margin:0 0 12px}
.tabs button{background:var(--panel2);color:var(--ink);border:1px solid var(--line);border-radius:6px;padding:6px 14px;font:inherit;cursor:pointer}
.tabs button.on{background:var(--accent);color:#08121c;border-color:var(--accent);font-weight:600}
.desc{background:#1b2838;border-radius:6px;padding:18px 20px;margin-bottom:12px;color:#d6d7d8}
.desc h1{font-size:22px;color:#fff;margin:4px 0 10px}
.desc h2{font-size:18px;color:#fff;margin:14px 0 6px}
.desc a{color:#66c0f4}
.desc p{margin:0}
.desc .gap{height:12px}
.desc ul,.desc ol{margin:4px 0 4px 22px;padding:0}
.desc .figure{margin:4px 0;position:relative}
.desc img.card{display:block;max-width:100%;border-radius:4px}
.desc .slot{position:absolute;top:8px;right:8px;background:rgba(0,0,0,.7);color:var(--warn);font:12px Consolas,monospace;padding:2px 6px;border-radius:4px}
.note{border-left:3px solid var(--warn);padding:6px 12px;color:var(--muted);margin:0 0 12px}
.checks{margin:0 0 12px;padding-left:20px}
</style>
</head>
<body>
<main>
<h1 class="page">MapGen AI 창작마당 검토</h1>
<p class="sub">올릴 파일과 새 소개글을 한 화면에 모은 검토용 페이지입니다. 올릴 파일은 모두 <code>upload</code> 폴더에 있습니다. 아래 경로는 이 페이지가 있는 폴더 기준입니다.</p>

<section>
<h2>1. Imgur에 올린 카드 4장</h2>
<p class="when">올리기 끝. 소개글 두 파일에 각 카드의 Imgur 직접 주소를 넣었습니다.</p>
<div class="path">__IMGUR_DIR__</div>
<div class="grid">__IMGUR_ITEMS__</div>
</section>

<section>
<h2>2. 소개글 미리보기</h2>
<p class="when">새 빌드를 올리는 날 스팀 웹 페이지의 설명 편집에 붙여 넣습니다. 카드는 소개글에 넣은 Imgur 주소에서 그대로 불러오고, 오른쪽 위에 그 주소를 표시했습니다.</p>
<div class="path">__DESC_FILES__</div>
<div class="tabs"><button class="on" data-tab="en">English</button><button data-tab="ko">한국어</button></div>
<div class="desc" id="desc-en">__DESC_EN__</div>
<div class="desc" id="desc-ko" hidden>__DESC_KO__</div>
</section>

<section>
<h2>3. 스팀 갤러리 사진 4장</h2>
<p class="when">새 빌드를 올리는 날 스팀 페이지의 이미지·영상 추가 편집에서 옛 스크린샷 대신 올립니다.</p>
<div class="path">__GALLERY_DIR__</div>
<div class="grid">__GALLERY_ITEMS__</div>
</section>

<section>
<h2>4. 표지</h2>
<p class="when">직접 올리지 않습니다. 일반판으로 승격할 때 모드의 About 폴더 Preview.png로 들어가고, 게임 업로더가 표지로 씁니다.</p>
<div class="path">__COVER_PATH__</div>
<div class="cover-row"><img class="full" src="__COVER_SRC__" alt="Cover A"><div class="small"><img src="__COVER_SRC__" alt="Cover A at list size">창작마당 목록에서 보이는 크기(204×115)</div></div>
</section>

<section>
<h2>5. 영상 시안 76초</h2>
<p class="when">순서와 자막을 확인하는 시안입니다. 실제 캡처 정지 화면으로 만들었고, 확정하면 움직이는 녹화로 바꿔 YouTube에 올립니다.</p>
<div class="path">__VIDEO_PATH__</div>
<video src="__VIDEO_SRC__" controls preload="metadata"></video>
</section>

<section>
<h2>이번 확인 결과</h2>
<ul class="checks">
<li>온천을 더하기 전후로 강물 칸의 위치가 한 칸도 바뀌지 않았습니다.</li>
<li>영어 화면의 대화 문구에 한국어가 나오지 않았습니다.</li>
<li>강을 북쪽으로 옮기는 요청은 여전히 실패해서, 소개글에서 강 위치를 옮긴다는 문구를 뺐습니다.</li>
</ul>
<p class="note">판정 전체: <code>captures/showcase-02/basic-checks.md</code></p>
</section>
</main>
<script>
document.querySelectorAll('.tabs button').forEach(function (b) {
  b.addEventListener('click', function () {
    document.querySelectorAll('.tabs button').forEach(function (x) { x.classList.toggle('on', x === b); });
    document.getElementById('desc-en').hidden = b.dataset.tab !== 'en';
    document.getElementById('desc-ko').hidden = b.dataset.tab !== 'ko';
  });
});
</script>
</body>
</html>
"""


def item(target, what):
    name = target.rsplit("/", 1)[-1]
    return (f'<div class="item"><a href="{target}" target="_blank"><img src="{target}" alt="{html.escape(name)}"></a>'
            f'<div class="name">{html.escape(name)}</div><div class="what">{html.escape(what)}</div></div>')


def main():
    card_paths = collect_uploads()
    imgur = [(f"upload/{g}/{n}", w) for g, n, s, w in UPLOADS if g == "1-imgur"]
    gallery = [(f"upload/{g}/{n}", w) for g, n, s, w in UPLOADS if g == "3-steam-gallery"]
    cover = "upload/2-cover/Preview.png"
    page = PAGE
    for key, value in {
        "__IMGUR_DIR__": "upload/1-imgur",
        "__IMGUR_ITEMS__": "".join(item(t, "소개글의 " + w + " 자리") for t, w in imgur),
        "__DESC_FILES__": "description-en.txt  ·  description-ko.txt",
        "__DESC_EN__": render_description("description-en.txt", card_paths),
        "__DESC_KO__": render_description("description-ko.txt", card_paths),
        "__GALLERY_DIR__": "upload/3-steam-gallery",
        "__GALLERY_ITEMS__": "".join(item(t, w) for t, w in gallery),
        "__COVER_PATH__": cover,
        "__COVER_SRC__": cover,
        "__VIDEO_PATH__": VIDEO,
        "__VIDEO_SRC__": VIDEO,
    }.items():
        if key not in page:
            raise SystemExit(f"template slot missing: {key}")
        page = page.replace(key, value)
    open(kit("review.html"), "w", encoding="utf-8", newline="\n").write(page)
    print(f"review.html written; {len(UPLOADS)} files in upload/")


if __name__ == "__main__":
    main()
