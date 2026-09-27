"""영상 시안용 장면 이미지. 실제 캡처를 자르거나 실제 Map Preview 이미지를 배치만 한다(그림 생성·합성 UI 없음).
사용: py make_frames.py  → frames/*.png (1920×1080)"""
from PIL import Image, ImageDraw, ImageFont
CAP = "../captures/showcase-01/"; OUT = "frames/"; W, H = 1920, 1080
BG = (23, 34, 49); INK = (243, 241, 234); MUTED = (174, 188, 203); BLUE = (108, 182, 255); BUBBLE = (38, 90, 140)
bold = lambda s: ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", s)
reg = lambda s: ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", s)
def preview(name, size):
    return Image.open(CAP + name).convert("RGB").resize((size, size), Image.NEAREST)
def crop16x9(src, box, out):
    Image.open(CAP + src).convert("RGB").crop(box).resize((W, H), Image.LANCZOS).save(OUT + out)
def wrap(d, text, font, width):
    words, lines, cur = text.split(), [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if d.textlength(t, font=font) <= width: cur = t
        else: lines.append(cur); cur = w
    return lines + [cur]
def heading(d, text):
    f = bold(58); tw = d.textlength(text, font=f); d.text(((W - tw) / 2, 42), text, font=f, fill=INK)
def before_after(a, b, la, lb, out, head):
    im = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(im); s = 700; top = 165
    heading(d, head)
    for x, name, lab in ((190, a, la), (W - 190 - s, b, lb)):
        im.paste(preview(name, s), (x, top)); d.rectangle((x - 3, top - 3, x + s + 2, top + s + 2), outline=BLUE, width=3)
        f = bold(38); tw = d.textlength(lab, font=f); d.text((x + (s - tw) / 2, top + s + 24), lab, font=f, fill=MUTED)
    cx, cy = W / 2, top + s / 2; d.polygon([(cx - 26, cy - 40), (cx - 26, cy + 40), (cx + 38, cy)], fill=BLUE)
    im.save(OUT + out)
def request_step(name, request, n, out, head):
    im = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(im); s = 780; top = 175
    heading(d, head)
    im.paste(preview(name, s), (W - 150 - s, top)); d.rectangle((W - 153 - s, top - 3, W - 148, top + s + 2), outline=BLUE, width=3)
    f = bold(46); lines = wrap(d, '"' + request + '"', f, 700); bh = 60 * len(lines) + 56; y0 = top + s / 2 - bh / 2
    d.rounded_rectangle((150, y0, 910, y0 + bh), radius=26, fill=BUBBLE)
    for i, ln in enumerate(lines): d.text((180, y0 + 28 + 60 * i), ln, font=f, fill=(255, 255, 255))
    d.text((150, y0 - 70), f"STEP {n}", font=bold(38), fill=BLUE)
    im.save(OUT + out)
def panel(src, box, out, head):
    im = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(im); heading(d, head)
    c = Image.open(CAP + src).convert("RGB").crop(box); mw, mh = 1680, 860
    k = min(mw / c.width, mh / c.height); c = c.resize((round(c.width * k), round(c.height * k)), Image.LANCZOS)
    x, y = (W - c.width) // 2, 165 + (mh - c.height) // 2; im.paste(c, (x, y)); d.rectangle((x - 3, y - 3, x + c.width + 2, y + c.height + 2), outline=BLUE, width=3)
    im.save(OUT + out)
panel("01-open.png", (0, 0, 1920, 1080), "01-open.png", "Pick a tile. Open AI Map Gen.")
panel("03-ring.png", (110, 0, 1390, 720), "02-request.png", "Describe the map you want")          # 한·영 병기 안내(720px 아래)는 잘라 낸다
before_after("01-open-preview.png", "03-ring-preview.png", "Before", "After", "03-result.png", "Map Preview shows the result")
request_step("04-island-preview.png", "Add a small island in the middle of the lake.", 2, "04-island.png", "Keep editing. Earlier changes stay.")
request_step("05-island-move-preview.png", "Move the island to the north side of the lake.", 3, "05-move.png", "Move what you made")
before_after("13-coast-before-preview.png", "13-coast-preview.png", "Before", '"Put the coast on the north side."', "06-coast.png", "Turn a coast. World connections stay.")
panel("11-recommend.png", (70, 90, 1470, 900), "07-ideas.png", "Or ask for ideas. Up to three, drawn by Map Preview.")
before_after("11-recommend-option-2.png", "12-refine-option-2.png", "Option 2", '"Make option 2 more natural."', "08-refine.png", "Refine one before you pick")
panel("02-guide.png", (250, 40, 1270, 670), "09-guide.png", "Not sure? A few questions, no AI calls")
crop16x9("14-map-1.png", (0, 40, 1690, 991), "10-map.png")
print("frames written")
