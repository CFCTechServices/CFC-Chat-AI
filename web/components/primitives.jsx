// Shared UI primitives
(() => {
  function Card({ children, className = '' }) {
    return <div className={`card ${className}`}>{children}</div>;
  }

  function PrimaryButton({ children, ...props }) {
    return (
      <button className="btn-primary" {...props}>
        {children}
      </button>
    );
  }

  function SecondaryButton({ children, ...props }) {
    return (
      <button className="btn-secondary" {...props}>
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

  window.CFC = window.CFC || {};
  window.CFC.Primitives = { Card, PrimaryButton, SecondaryButton, TextInput };
})();
