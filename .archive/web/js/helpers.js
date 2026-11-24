// Shared DOM and utility helpers for vanilla JS features
export const createItem = (name) => {
  const el = document.createElement('div');
  el.className = 'item';
  el.innerHTML = `
    <div class="row">
      <div class="name">${name}</div>
      <div class="meta" data-status>Ready</div>
    </div>
    <div class="bar"><div class="fill" data-fill></div></div>
    <div class="meta" data-detail></div>
  `;
  return el;
};

export const setProgress = (el, pct) => {
  const fill = el.querySelector('[data-fill]');
  if (fill) fill.style.width = `${pct}%`;
};

export const setStatus = (el, text, ok = false) => {
  const st = el.querySelector('[data-status]');
  if (st) {
    st.textContent = text;
    st.className = `meta ${ok ? 'ok' : ''}`;
  }
};

export const setDetail = (el, text, isError = false) => {
  const d = el.querySelector('[data-detail]');
  if (d) {
    d.textContent = text;
    d.className = `meta ${isError ? 'err' : ''}`;
  }
};

export const formatTimestamp = (seconds) => {
  if (seconds === undefined || seconds === null || Number.isNaN(seconds)) return null;
  const total = Math.max(0, Math.floor(Number(seconds)));
  const hrs = Math.floor(total / 3600);
  const mins = Math.floor((total % 3600) / 60);
  const secs = total % 60;
  const parts = hrs ? [hrs, mins, secs] : [mins, secs];
  return parts.map((part, idx) => {
    if (idx === 0 && !hrs) return String(part).padStart(2, '0');
    return String(part).padStart(2, '0');
  }).join(':');
};

export const appendMsg = (thread, text, who = 'bot', extraClass = '') => {
  const div = document.createElement('div');
  div.className = `msg ${who} ${extraClass}`.trim();
  div.textContent = text;
  thread.appendChild(div);
  thread.scrollTop = thread.scrollHeight;
  return div;
};

export const appendVideoCard = (thread, clip) => {
  if (!clip || !clip.video_url) return;
  const card = document.createElement('div');
  card.className = 'msg bot video-card';

  const label = document.createElement('div');
  label.className = 'video-label';
  label.textContent = 'Video reference';

  const time = document.createElement('div');
  time.className = 'video-time';
  const ts = clip.timestamp || formatTimestamp(clip.start_seconds) || 'Timestamp unavailable';
  const endTs = clip.end_timestamp || (clip.end_seconds != null ? formatTimestamp(clip.end_seconds) : null);
  time.textContent = endTs && endTs !== ts ? `${ts} → ${endTs}` : ts;

  const link = document.createElement('a');
  link.href = clip.deep_link_url || clip.video_url;
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  link.className = 'video-link';
  link.textContent = 'Open video';

  const preview = document.createElement('p');
  preview.className = 'video-preview';
  preview.textContent = clip.preview || '';

  card.appendChild(label);
  card.appendChild(time);
  card.appendChild(link);
  if (preview.textContent) card.appendChild(preview);

  thread.appendChild(card);
  thread.scrollTop = thread.scrollHeight;
};
