// Shared UI primitives
(() => {
  function Card({ children, className = '' }) {
    return <div className={`card ${className}`}>{children}</div>;
  }

  function PrimaryButton({ children, className = '', ...props }) {
    return (
      <button className={`btn-primary ${className}`} {...props}>
        {children}
      </button>
    );
  }

  function SecondaryButton({ children, className = '', ...props }) {
    return (
      <button className={`btn-secondary ${className}`} {...props}>
        {children}
      </button>
    );
  }

  function TextInput({ label, error, ...props }) {
    return (
      <label className="field">
        <span className="field-label">{label}</span>
        <input className={`field-input ${error ? 'field-error' : ''}`} {...props} />
        {error && <span className="field-error-text">{error}</span>}
      </label>
    );
  }

  // ── Toast ──────────────────────────────────────────────────────────────────
  // Usage: window.CFC.toast('message')  or  window.CFC.toast('msg', 'warning')
  // Types: 'success' | 'warning' | 'error' | 'info'

  const TOAST_COLORS = {
    success: { bg: 'rgba(55,157,99,0.95)', border: '#16a34a', icon: '✓' },
    warning: { bg: 'rgba(161,98,7,0.95)',  border: '#ca8a04', icon: '⚠' },
    error:   { bg: 'rgba(185,28,28,0.95)', border: '#dc2626', icon: '✕' },
    info:    { bg: 'rgba(30,41,59,0.97)',  border: '#475569', icon: 'ℹ' },
  };

  let _toastRoot = null;
  function _getRoot() {
    if (_toastRoot) return _toastRoot;
    _toastRoot = document.createElement('div');
    Object.assign(_toastRoot.style, {
      position: 'fixed', bottom: '24px', right: '24px',
      zIndex: '99999', display: 'flex', flexDirection: 'column', gap: '10px',
      maxWidth: '420px', pointerEvents: 'none',
    });
    document.body.appendChild(_toastRoot);
    return _toastRoot;
  }

  function toast(message, type = 'info', duration = 6000) {
    const root = _getRoot();
    const cfg = TOAST_COLORS[type] || TOAST_COLORS.info;

    const el = document.createElement('div');
    Object.assign(el.style, {
      display: 'flex', alignItems: 'flex-start', gap: '10px',
      padding: '12px 16px', borderRadius: '8px',
      background: cfg.bg, border: `1px solid ${cfg.border}`,
      color: '#f8fafc', fontSize: '0.88rem', lineHeight: '1.4',
      boxShadow: '0 4px 20px rgba(0,0,0,0.35)',
      pointerEvents: 'auto', cursor: 'pointer',
      opacity: '0', transition: 'opacity 0.2s ease',
    });

    const icon = document.createElement('span');
    icon.textContent = cfg.icon;
    Object.assign(icon.style, { fontWeight: 'bold', flexShrink: '0', marginTop: '1px' });

    const text = document.createElement('span');
    text.textContent = message;

    el.appendChild(icon);
    el.appendChild(text);
    root.appendChild(el);

    requestAnimationFrame(() => { el.style.opacity = '1'; });

    const dismiss = () => {
      el.style.opacity = '0';
      setTimeout(() => el.remove(), 200);
    };
    el.addEventListener('click', dismiss);
    if (duration > 0) setTimeout(dismiss, duration);
  }

  window.CFC = window.CFC || {};
  window.CFC.Primitives = { Card, PrimaryButton, SecondaryButton, TextInput };
  window.CFC.toast = toast;
})();
