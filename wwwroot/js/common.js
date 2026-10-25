// ========== 共通ユーティリティ ==========

// 選択色（赤/緑/青）。2画面で共有するため localStorage に保持。
const COLORS = {
  red:   { r: 255, g: 0,   b: 0,   label: '赤', css: '#e53935' },
  green: { r: 0,   g: 255, b: 0,   label: '緑', css: '#43a047' },
  blue:  { r: 0,   g: 0,   b: 255, label: '青', css: '#1e88e5' },
};

function getColorKey() { return localStorage.getItem('demoColor') || 'red'; }
function setColorKey(k) { localStorage.setItem('demoColor', k); }
function currentColor() { const c = COLORS[getColorKey()]; return { r: c.r, g: c.g, b: c.b }; }

// テスター自身のエンドポイントを叩く（SynchrolightAPI へはサーバ側が中継）。
async function api(path, { method = 'POST', body } = {}) {
  const res = await fetch(path, {
    method,
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  let data = {};
  try { data = await res.json(); } catch { /* no body */ }
  if (!res.ok || data.ok === false) {
    const msg = data.message || `HTTP ${res.status}`;
    throw new Error(msg);
  }
  return data;
}

// ---- ログ ----
function initLog(el) { window.__log = el; }
function log(msg, kind = 'info') {
  const el = window.__log;
  if (!el) return;
  const t = new Date().toLocaleTimeString('ja-JP');
  const ln = document.createElement('div');
  ln.className = 'ln ' + kind;
  ln.textContent = `[${t}] ${msg}`;
  el.prepend(ln);
}

// ---- デモボタンの共通ハンドラ ----
// handler: async () => data   /  name: ログ表示名
async function runDemo(btn, name, handler) {
  const all = document.querySelectorAll('.tile, .btn.demo');
  all.forEach(b => b.disabled = true);
  try {
    const data = await handler();
    log(`${name}: ${data.message || 'OK'}`, 'ok');
    refreshRunning();
  } catch (e) {
    log(`${name}: ${e.message}`, 'err');
  } finally {
    all.forEach(b => b.disabled = false);
  }
}

// ---- 実行中デモ表示 ----
let _runningEl = null;
function initRunning(el) { _runningEl = el; refreshRunning(); setInterval(refreshRunning, 2500); }
async function refreshRunning() {
  if (!_runningEl) return;
  try {
    const d = await api('/api/demo/status', { method: 'GET' });
    const running = d.running === true && d.current && d.current !== 'idle';
    _runningEl.innerHTML =
      `<span class="pulse ${running ? 'live' : ''}"></span>` +
      `<span>実行中:</span> <span class="name">${running ? d.current : 'なし'}</span>`;
  } catch { /* ignore */ }
}

// ========== 色ピッカー ==========
function renderColorPicker(container, onChange) {
  const sel = getColorKey();
  container.innerHTML = `
    <div class="color-picker">
      ${Object.entries(COLORS).map(([k, c]) =>
        `<div class="swatch ${k} ${k === sel ? 'selected' : ''}" data-k="${k}" title="${c.label}">${c.label}</div>`).join('')}
      <span class="selected-color">選択中: <span id="selColorName"></span></span>
    </div>`;
  const nameEl = container.querySelector('#selColorName');
  const paint = () => {
    const c = COLORS[getColorKey()];
    nameEl.innerHTML = `<span class="dot" style="background:${c.css}"></span>${c.label}`;
  };
  container.querySelectorAll('.swatch').forEach(sw => {
    sw.addEventListener('click', () => {
      setColorKey(sw.dataset.k);
      container.querySelectorAll('.swatch').forEach(s => s.classList.remove('selected'));
      sw.classList.add('selected');
      paint();
      log(`色指定: ${COLORS[sw.dataset.k].label}（このボタンだけでは光りません）`, 'info');
      onChange && onChange();
    });
  });
  paint();
}

// ========== 接続パネル ==========
function renderConnectionBar(container) {
  container.innerHTML = `
    <div class="panel">
      <h2>接続設定 <span class="tag">SynchrolightAPI</span>
        <span id="connPill" class="status-pill">未確認</span></h2>
      <p class="hint">COMポートを選んで接続し、送信機を初期化します（SynchrolightAPI 既定 http://localhost:5100）。</p>
      <div class="conn-grid">
        <div>
          <div class="row" style="margin-bottom:.5rem">
            <button id="scanBtn" class="btn small">COMポート再スキャン</button>
            <span id="scanInfo" class="selected-color"></span>
          </div>
          <div id="ports" class="ports"><span class="selected-color">「再スキャン」を押してください</span></div>
        </div>
        <div class="row" style="align-items:flex-end">
          <label class="field">チャネル
            <select id="txCh"><option>1</option><option>2</option><option>3</option><option selected>4</option></select>
          </label>
          <label class="field">電力
            <select id="txPwr"><option selected>0</option><option>1</option><option>2</option><option>3</option></select>
          </label>
          <button id="connectBtn" class="btn" style="background:var(--accent);border-color:var(--accent);color:#fff">接続 + 初期化</button>
        </div>
      </div>
    </div>`;

  const portsEl = container.querySelector('#ports');
  const pill = container.querySelector('#connPill');

  function renderPorts(ports) {
    if (!ports || ports.length === 0) {
      portsEl.innerHTML = '<span class="selected-color">利用可能なCOMポートがありません</span>';
    } else {
      portsEl.innerHTML = ports.map((p, i) =>
        `<label class="port-chk"><input type="checkbox" value="${p}" ${i === 0 ? 'checked' : ''}>${p}</label>`).join('');
    }
    container.querySelector('#scanInfo').textContent = `${ports.length} 件`;
  }

  // 手動スキャン（1回）
  async function scan() {
    container.querySelector('#scanInfo').textContent = 'スキャン中...';
    try {
      const d = await api('/api/conn/scan', { method: 'GET' });
      renderPorts(d.ports);
      log(`COMポート: ${d.ports.join(', ') || 'なし'}`, 'info');
    } catch (e) {
      portsEl.innerHTML = `<span class="selected-color" style="color:var(--err)">${e.message}</span>`;
      container.querySelector('#scanInfo').textContent = '';
      log(`スキャン失敗: ${e.message}`, 'err');
    }
  }

  // 初回スキャン（SynchrolightAPI 自動起動の完了を待ちつつリトライ）
  async function initialScan() {
    const maxAttempts = 30;
    for (let i = 0; i < maxAttempts; i++) {
      try {
        const d = await api('/api/conn/scan', { method: 'GET' });
        renderPorts(d.ports);
        pill.textContent = 'API応答OK'; pill.className = 'status-pill on';
        log(`COMポート: ${d.ports.join(', ') || 'なし'}`, 'info');
        return;
      } catch (e) {
        pill.textContent = 'API起動待ち'; pill.className = 'status-pill';
        portsEl.innerHTML = `<span class="selected-color">SynchrolightAPI の起動を待っています... (${i + 1}/${maxAttempts})</span>`;
        await new Promise(r => setTimeout(r, 1500));
      }
    }
    pill.textContent = 'API未応答'; pill.className = 'status-pill off';
    portsEl.innerHTML = '<span class="selected-color" style="color:var(--err)">SynchrolightAPI に接続できません。自動起動に失敗した可能性があります（手動起動してください）。</span>';
    log('SynchrolightAPI に接続できませんでした。', 'err');
  }

  async function connect() {
    const ports = [...portsEl.querySelectorAll('input:checked')].map(c => c.value);
    if (ports.length === 0) { log('接続するCOMポートを選択してください。', 'err'); return; }
    const channel = parseInt(container.querySelector('#txCh').value, 10);
    const power = parseInt(container.querySelector('#txPwr').value, 10);
    const btn = container.querySelector('#connectBtn');
    btn.disabled = true;
    try {
      const d = await api('/api/conn/connect', { body: { ports, channel, power } });
      pill.textContent = '接続済み'; pill.className = 'status-pill on';
      log(`接続: ${d.message}`, 'ok');
    } catch (e) {
      pill.textContent = '接続失敗'; pill.className = 'status-pill off';
      log(`接続失敗: ${e.message}`, 'err');
    } finally { btn.disabled = false; }
  }

  container.querySelector('#scanBtn').addEventListener('click', scan);
  container.querySelector('#connectBtn').addEventListener('click', connect);
  initialScan();
}

// ---- ヘッダ nav のアクティブ表示 ----
function markNav(page) {
  document.querySelectorAll('.nav a').forEach(a => {
    if (a.dataset.page === page) a.classList.add('active');
  });
}
