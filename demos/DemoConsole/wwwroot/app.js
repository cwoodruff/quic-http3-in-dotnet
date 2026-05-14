'use strict';

const output = document.getElementById('output');
const caption = document.getElementById('caption');
const buttons = Array.from(document.querySelectorAll('button.btn'));
const buttonsByKey = Object.fromEntries(buttons.map(b => [b.dataset.key, b]));

let currentSource = null;
let activeButton = null;

function setCaption(text) { caption.textContent = text; }

function setActiveButton(btn) {
  if (activeButton) activeButton.classList.remove('active');
  activeButton = btn;
  if (btn) btn.classList.add('active');
}

function clearOutput(placeholder = 'No demo running.') {
  output.replaceChildren();
  const ph = document.createElement('div');
  ph.className = 'placeholder';
  ph.textContent = placeholder;
  output.append(ph);
  output.classList.add('empty');
}

function setOutput(node) {
  output.replaceChildren(node);
  output.classList.remove('empty');
}

function showError(message) {
  const div = document.createElement('div');
  div.className = 'error-banner';
  div.textContent = 'Error: ' + message;
  setOutput(div);
}

function closeStream() {
  if (currentSource) { currentSource.close(); currentSource = null; }
  setActiveButton(null);
  buttons.forEach(b => b.disabled = false);
}

function startStream(url) {
  closeStream();
  return new EventSource(url);
}

async function refreshHealth() {
  try {
    const r = await fetch('/health');
    const s = await r.json();
    document.getElementById('pill-http3').classList.toggle('ready', s.http3);
    document.getElementById('pill-quic').classList.toggle('ready', s.quic);
  } catch { /* keep prior state */ }
}
setInterval(refreshHealth, 1500);
refreshHealth();

// ---------- HTTP demos ----------

function renderHttpResponse(payload) {
  const tpl = document.getElementById('tpl-http-card');
  const node = tpl.content.firstElementChild.cloneNode(true);

  node.querySelector('.status').textContent = payload.status;
  const proto = node.querySelector('.protocol');
  proto.textContent = payload.negotiated;
  if (payload.negotiated.endsWith('3.0')) proto.classList.add('h3');
  else if (payload.negotiated.endsWith('2.0')) proto.classList.add('h2');
  else proto.classList.add('h1');

  node.querySelector('.requested').textContent = `${payload.requested}  (${payload.policy})`;
  node.querySelector('.altsvc').textContent = payload.altSvc ?? '(not present)';
  node.querySelector('.body').textContent = payload.body;
  node.querySelector('.elapsed').textContent = `${payload.elapsedMs} ms`;

  setOutput(node);
}

function runHttpDemo(version) {
  const which = version === 2 ? 'http2' : 'http3';
  setCaption(`Sending HTTP/${version} request to https://localhost:5051/ …`);
  clearOutput('Awaiting response …');

  const src = startStream(`/demo/${which}`);
  currentSource = src;

  src.addEventListener('status', e => setCaption(e.data));
  src.addEventListener('response', e => {
    const payload = JSON.parse(e.data);
    renderHttpResponse(payload);
    setCaption(`Negotiated ${payload.negotiated} in ${payload.elapsedMs} ms`);
  });
  src.addEventListener('error', e => {
    if (e.data) showError(e.data);
  });
  src.addEventListener('done', () => closeStream());
  src.onerror = () => closeStream();
}

// ---------- Multiplex demo ----------

function runMultiplexDemo() {
  setCaption('Multiplexed transfer: 200 MB file + 1 s heartbeat on one connection.');

  const tpl = document.getElementById('tpl-multiplex');
  const node = tpl.content.firstElementChild.cloneNode(true);
  const heartbeatLog = node.querySelector('.panel-heartbeat .panel-log');
  const fileLog      = node.querySelector('.panel-file .panel-log');
  const bar          = node.querySelector('.panel-progress .bar');
  const barLabel     = node.querySelector('.panel-progress .bar-label');
  setOutput(node);

  const totalMb = 200;
  let lastFileMb = 0;

  const append = (list, text) => {
    const li = document.createElement('li');
    li.textContent = text;
    list.appendChild(li);
    list.scrollTop = list.scrollHeight;
  };

  const src = startStream(`/demo/multiplex?size=${totalMb}&ticks=30`);
  currentSource = src;

  src.addEventListener('status', e => setCaption(e.data));

  src.addEventListener('heartbeat', e => {
    append(heartbeatLog, stripTag(e.data));
  });

  src.addEventListener('file', e => {
    const text = stripTag(e.data);
    append(fileLog, text);
    const match = /uploaded\s+(\d+)/i.exec(text) ?? /complete:\s+([\d.]+)/i.exec(text);
    if (match) {
      const mb = Math.min(totalMb, Math.round(parseFloat(match[1])));
      lastFileMb = Math.max(lastFileMb, mb);
      bar.style.width = `${(lastFileMb / totalMb) * 100}%`;
      barLabel.textContent = `${lastFileMb} / ${totalMb} MB`;
    }
    if (/complete/i.test(text)) {
      bar.style.width = '100%';
      barLabel.textContent = `${totalMb} / ${totalMb} MB ✓`;
    }
  });

  src.addEventListener('stdout', e => {
    // generic client log lines — show them on whichever panel makes sense
    const text = e.data;
    if (/connected/i.test(text)) setCaption(text);
  });

  src.addEventListener('stderr', e => append(fileLog, '! ' + e.data));
  src.addEventListener('exit', e => setCaption(`Client exited (code ${e.data}).`));
  src.addEventListener('error', e => { if (e.data) showError(e.data); });
  src.addEventListener('done', () => closeStream());
  src.onerror = () => closeStream();
}

function stripTag(line) {
  // "[client][heartbeat] tick 01 sent ..." → "tick 01 sent ..."
  return line.replace(/^\[client\]\[(heartbeat|file)\]\s*/, '');
}

// ---------- Wiring ----------

function trigger(btn) {
  if (!btn || btn.disabled) return;
  const demo = btn.dataset.demo;
  if (!demo) { // clear button
    closeStream();
    clearOutput();
    setCaption('Press 1, 2, or 3 to run a demo.');
    return;
  }
  setActiveButton(btn);
  buttons.forEach(b => { if (b.dataset.demo) b.disabled = true; });
  if (demo === 'http2') runHttpDemo(2);
  else if (demo === 'http3') runHttpDemo(3);
  else if (demo === 'multiplex') runMultiplexDemo();
}

buttons.forEach(b => b.addEventListener('click', () => trigger(b)));

document.addEventListener('keydown', (e) => {
  if (e.metaKey || e.ctrlKey || e.altKey) return;
  if (document.activeElement && document.activeElement.tagName === 'INPUT') return;
  const btn = buttonsByKey[e.key.toLowerCase()];
  if (btn) { e.preventDefault(); trigger(btn); }
});
