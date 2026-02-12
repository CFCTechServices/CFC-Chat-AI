(() => {
  function Toggle({ checked, onChange }) {
    return (
      <button
        className={`toggle ${checked ? 'checked' : ''}`}
        onClick={() => onChange(!checked)}
        type="button"
        role="switch"
        aria-checked={checked}
      >
        <span className="toggle-track">
          <span className="toggle-thumb" />
        </span>
      </button>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.Toggle = Toggle;
})();