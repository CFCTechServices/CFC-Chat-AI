(() => {
  function TabButton({ active, onClick, icon, label }) {
    return (
      <button
        className={`tab-button ${active ? 'active' : ''}`}
        onClick={onClick}
        type="button"
      >
        {icon && <span className="tab-icon">{icon}</span>}
        <span>{label}</span>
      </button>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.TabButton = TabButton;
})();