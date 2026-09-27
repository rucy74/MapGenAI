// 창작마당 카드·표지 렌더러. 외부 패키지 없이 헤드리스 Chrome DevTools 프로토콜만 쓴다.
// 사용: node build_images.mjs <chrome.exe> <page.html> <출력폴더> <요소id,콤마> [CSS폭=640] [배율=1.5]
// 결과: <출력폴더>/<id>.png (폭 = CSS폭×배율), <출력폴더>/render-checks-<page>.json
import { spawn } from 'node:child_process';
import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os';
const [chrome, html, outDir, idsArg, widthArg, scaleArg] = process.argv.slice(2);
if (!chrome || !html || !outDir || !idsArg) { console.error('usage: node build_images.mjs <chrome> <html> <outDir> <ids> [width] [scale]'); process.exit(2); }
const cssWidth = Number(widthArg || 640), scale = Number(scaleArg || 1.5);
const ids = idsArg.split(',').map(s => s.trim()).filter(Boolean);
fs.mkdirSync(outDir, { recursive: true });
const port = 9300 + Math.floor(Math.random() * 500);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'mapgen-cards-'));
const proc = spawn(chrome, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--hide-scrollbars', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--allow-file-access-from-files', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function json(url) { for (let i = 0; i < 100; i++) { try { const r = await fetch(url); return await r.json(); } catch { await sleep(150); } } throw new Error('DevTools endpoint not reachable'); }
let ws, seq = 0; const pending = new Map();
function send(method, params = {}) { return new Promise((res, rej) => { const id = ++seq; pending.set(id, { res, rej }); ws.send(JSON.stringify({ id, method, params })); }); }
async function evaluate(expr) { const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true }); if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails)); return r.result.value; }
const OVERFLOW = `(root) => { const bad = []; const rr = root.getBoundingClientRect();
  for (const el of root.querySelectorAll('*')) { const cs = getComputedStyle(el); if (cs.display === 'none' || el.closest('[data-allow-overflow]')) continue;
    const b = el.getBoundingClientRect(); if (b.width === 0 && b.height === 0) continue;
    if (el.scrollWidth > el.clientWidth + 1 && cs.overflowX === 'visible' && el.children.length === 0) bad.push({ tag: el.tagName, text: (el.textContent||'').slice(0,60), kind: 'text wider than box' });
    if (b.right > rr.right + 1 || b.left < rr.left - 1) bad.push({ tag: el.tagName, text: (el.textContent||'').slice(0,60), kind: 'outside card' }); }
  return bad; }`;
try {
  const list = await json(`http://127.0.0.1:${port}/json/list`);
  const page = list.find(t => t.type === 'page');
  ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.addEventListener('open', res, { once: true }); ws.addEventListener('error', rej, { once: true }); });
  ws.addEventListener('message', ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { const { res, rej } = pending.get(m.id); pending.delete(m.id); m.error ? rej(new Error(JSON.stringify(m.error))) : res(m.result); } });
  await send('Page.enable'); await send('Runtime.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: cssWidth, height: 1200, deviceScaleFactor: scale, mobile: false });
  await send('Page.navigate', { url: 'file:///' + path.resolve(html).split(String.fromCharCode(92)).join('/') });
  for (let i = 0; i < 200; i++) { if (await evaluate(`document.readyState === 'complete' && document.fonts.status === 'loaded' && [...document.images].every(i => i.complete)`)) break; await sleep(100); }
  await sleep(300);
  // 검출기 자체 시험: 넘치는 표본을 넣으면 반드시 잡혀야 한다
  const selfTest = await evaluate(`(() => { const box = document.createElement('div'); box.style.cssText='width:60px'; const t = document.createElement('span'); t.style.cssText='display:inline-block;width:40px;white-space:nowrap'; t.textContent='Unbreakablesupercalifragilistic'; box.appendChild(t); document.body.appendChild(box); const hit = (${OVERFLOW})(box).length > 0; box.remove(); return hit; })()`);
  if (!selfTest) throw new Error('overflow detector self-test failed');
  const images = await evaluate(`[...document.images].map(i => ({ src: i.getAttribute('src'), ok: i.complete && i.naturalWidth > 0, w: i.naturalWidth, h: i.naturalHeight }))`);
  const results = [];
  for (const id of ids) {
    const box = await evaluate(`(() => { const e = document.getElementById(${JSON.stringify(id)}); if (!e) return null; const b = e.getBoundingClientRect(); return { x: b.left + scrollX, y: b.top + scrollY, w: b.width, h: b.height }; })()`);
    if (!box) { results.push({ id, error: 'element not found' }); continue; }
    const overflow = await evaluate(`(${OVERFLOW})(document.getElementById(${JSON.stringify(id)}))`);
    const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true, clip: { x: box.x, y: box.y, width: box.w, height: box.h, scale: 1 } });
    const out = path.join(outDir, id + '.png'); fs.writeFileSync(out, Buffer.from(shot.data, 'base64'));
    results.push({ id, file: out, cssBox: box, pixelWidth: Math.round(box.w * scale), pixelHeight: Math.round(box.h * scale), overflow });
  }
  const report = { html: path.resolve(html), chrome, cssWidth, scale, overflowSelfTest: selfTest, images, results, created: new Date().toISOString() };
  fs.writeFileSync(path.join(outDir, 'render-checks-' + path.basename(html, '.html') + '.json'), JSON.stringify(report, null, 2));
  const badImages = images.filter(i => !i.ok), badOverflow = results.filter(r => r.overflow && r.overflow.length);
  console.log(`rendered ${results.filter(r => r.file).length}/${ids.length}; images ${images.length - badImages.length}/${images.length} ok; overflow in ${badOverflow.length} element(s); selfTest=${selfTest}`);
  for (const b of badImages) console.log('  IMAGE FAIL', b.src);
  for (const r of badOverflow) console.log('  OVERFLOW', r.id, JSON.stringify(r.overflow.slice(0, 3)));
  process.exitCode = (badImages.length || badOverflow.length || results.some(r => r.error)) ? 1 : 0;
} finally { try { ws && ws.close(); } catch {} proc.kill(); await sleep(300); try { fs.rmSync(profile, { recursive: true, force: true }); } catch {} }
