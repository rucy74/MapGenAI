"""Steam 서식(BBCode) 검사: 태그 짝·중첩, 이미지 자리표시자, 링크 목록. 사용: py check_bbcode.py <파일...>"""
import re, sys
PAIRED = {"h1","h2","h3","b","i","u","strike","list","olist","url","img","quote","spoiler","noparse","table","tr","td","th","code"}
TAG = re.compile(r"\[(/?)([a-z0-9]+)(?:=[^\]]*)?\]")
def check(text):
    stack, errors = [], []
    for m in TAG.finditer(text):
        closing, name = m.group(1) == "/", m.group(2)
        if name == "*":
            continue
        if name not in PAIRED:
            continue
        if not closing:
            stack.append((name, m.start()))
        elif not stack or stack[-1][0] != name:
            errors.append(f"unexpected [/{name}] at {m.start()} (open: {stack[-1][0] if stack else None})")
        else:
            stack.pop()
    errors += [f"unclosed [{n}] at {p}" for n, p in stack]
    return errors
# 양성 대조: 잘못된 중첩과 닫히지 않은 태그를 반드시 잡아야 한다
assert check("[b]x[/h2]") and check("[list][*]a") and not check("[h1]a[/h1][list][*]b[/list]"), "checker self-test failed"
ok = True
for path in sys.argv[1:]:
    t = open(path, encoding="utf-8").read()
    errs = check(t)
    imgs = re.findall(r"\[img\](.*?)\[/img\]", t)
    urls = re.findall(r"\[url=([^\]]+)\]", t)
    print(f"{path}: errors={len(errs)} imgs={imgs} links={len(urls)}")
    for u in urls: print("   ", u)
    for e in errs: print("  ERROR", e); ok = False
sys.exit(0 if ok else 1)
