"""정지 캡처로 만드는 영상 시안(애니매틱). 사용: py build_animatic.py shots.json out.mp4
shots.json = {"fps":30,"size":[1920,1080],"fade":0.4,"shots":[{"image":경로|null,"card":{"title":..,"subtitle":..}|null,
 "seconds":6,"caption":"...","zoom":[시작배율,끝배율],"focus":[x비율,y비율]}]}
실제 게임 캡처만 넣는다. 자막은 화면 아래 띠에 그린다. 프레임을 ffmpeg 표준입력으로 흘려 H.264로 인코딩한다."""
import json, subprocess, sys
from PIL import Image, ImageDraw, ImageFont
import imageio_ffmpeg
spec = json.load(open(sys.argv[1], encoding="utf-8")); out = sys.argv[2]
W, H = spec.get("size", [1920, 1080]); FPS = spec.get("fps", 30); FADE = spec.get("fade", 0.4)
FONT = "C:/Windows/Fonts/segoeuib.ttf"; TITLE_FONT = "C:/Windows/Fonts/bahnschrift.ttf"
cap_font = ImageFont.truetype(FONT, 46); small_font = ImageFont.truetype(FONT, 30)
def title_font(size):
    f = ImageFont.truetype(TITLE_FONT, size)
    try: f.set_variation_by_name("Bold")
    except Exception: pass
    return f
def card_frame(card):
    im = Image.new("RGB", (W, H), (23, 34, 49)); d = ImageDraw.Draw(im)
    t = card.get("title", ""); st = card.get("subtitle", "")
    tf = title_font(150); parts = t.split("|")  # "MAPGEN |AI" -> 두 번째 조각은 파란색
    widths = [d.textlength(p, font=tf) for p in parts]; x = (W - sum(widths)) / 2; y = H * 0.36
    for p, w, col in zip(parts, widths, [(243, 241, 234), (108, 182, 255)]):
        d.text((x, y), p, font=tf, fill=col); x += w
    if st:
        sf = title_font(46); sw = d.textlength(st, font=sf); d.text(((W - sw) / 2, y + 190), st, font=sf, fill=(244, 190, 99))
    return im
def base_image(shot):
    if shot.get("card"): return card_frame(shot["card"])
    im = Image.open(shot["image"]).convert("RGB")
    if im.size != (W, H):
        s = max(W / im.width, H / im.height); im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
        im = im.crop(((im.width - W) // 2, (im.height - H) // 2, (im.width - W) // 2 + W, (im.height - H) // 2 + H))
    return im
def zoomed(im, z, fx, fy):
    if z <= 1.0001: return im.copy()
    cw, ch = W / z, H / z; cx = min(max(fx * W, cw / 2), W - cw / 2); cy = min(max(fy * H, ch / 2), H - ch / 2)
    return im.crop((round(cx - cw / 2), round(cy - ch / 2), round(cx + cw / 2), round(cy + ch / 2))).resize((W, H), Image.BILINEAR)
def with_caption(im, text, pos="bottom"):
    if not text: return im
    im = im.copy(); d = ImageDraw.Draw(im, "RGBA"); tw = d.textlength(text, font=cap_font)
    bw, bh = tw + 80, 92; x0 = (W - bw) / 2; y0 = 60 if pos == "top" else H - bh - 70
    d.rounded_rectangle((x0, y0, x0 + bw, y0 + bh), radius=14, fill=(12, 18, 26, 205))
    d.text((x0 + 40, y0 + 17), text, font=cap_font, fill=(243, 241, 234))
    return im
frames_total = 0
ff = imageio_ffmpeg.get_ffmpeg_exe()
proc = subprocess.Popen([ff, "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
                         "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", out], stdin=subprocess.PIPE)
prev_last = None
for shot in spec["shots"]:
    base = base_image(shot); n = max(1, round(shot["seconds"] * FPS)); z0, z1 = shot.get("zoom", [1.0, 1.0]); fx, fy = shot.get("focus", [0.5, 0.5])
    nfade = round(FADE * FPS) if prev_last is not None else 0
    for i in range(n):
        t = i / max(1, n - 1); z = z0 + (z1 - z0) * (t * t * (3 - 2 * t))
        fr = with_caption(zoomed(base, z, fx, fy), shot.get("caption", ""), shot.get("caption_pos", "bottom"))
        if i < nfade: fr = Image.blend(prev_last, fr, (i + 1) / (nfade + 1))
        proc.stdin.write(fr.tobytes()); frames_total += 1
        last = fr
    prev_last = last
proc.stdin.close(); code = proc.wait()
print(json.dumps({"out": out, "frames": frames_total, "seconds": round(frames_total / FPS, 2), "ffmpeg_exit": code}))
sys.exit(code)
