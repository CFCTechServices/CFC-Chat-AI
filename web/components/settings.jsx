// User Settings page with Profile and Preferences tabs
(() => {
  const { Layout } = window.CFC.Layout;
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { useUser } = window.CFC.UserContext;

  function SettingsPage() {
    const { user } = useUser();

    const [activeTab, setActiveTab] = React.useState('profile'); // 'profile' | 'preferences'

    // Profile state
    const [fullName, setFullName] = React.useState(user?.name || '');
    const [email] = React.useState(user?.email || '');
    const [role] = React.useState(user?.role || 'User');
    const [avatar, setAvatar] = React.useState(user?.avatar || '');

    // Preferences state
    const [startingPage, setStartingPage] = React.useState(() => {
      try {
        return window.localStorage.getItem('cfc-starting-page') || 'chat';
      } catch {
        return 'chat';
      }
    });
    const [notificationsEnabled, setNotificationsEnabled] = React.useState(() => {
      try {
        return window.localStorage.getItem('cfc-notifications') === 'on';
      } catch {
        return true;
      }
    });
    const [textSize, setTextSize] = React.useState(() => {
      try { return window.localStorage.getItem('cfc-text-size') || 'medium'; } catch { return 'medium'; }
    });


    const { setUser } = useUser();

    const saveProfile = () => {
      const next = { ...(user || {}), name: fullName || (user?.name || ''), email: email || (user?.email || ''), role: role || (user?.role || 'User') };
      if (avatar) next.avatar = avatar;
      setUser(next);
      alert('Profile saved locally.');
    };

    const savePreferences = () => {
      try {
        window.localStorage.setItem('cfc-starting-page', startingPage);
        window.localStorage.setItem('cfc-notifications', notificationsEnabled ? 'on' : 'off');
      } catch {}
      alert('Preferences saved (placeholder).');
    };

    const CP = window.CFC && window.CFC.ChangePasswordPlaceholder;
    return (
      <Layout>
        <div className="page settings-page">
          <div className="page-header-row">
            <div>
              <h1>Settings</h1>
              <p>Manage your profile and application preferences</p>
            </div>
          </div>

          <div className="admin-tabs">
            <button
              type="button"
              className={`tab-button ${activeTab === 'profile' ? 'active' : ''}`}
              onClick={() => setActiveTab('profile')}
            >
              <span className="tab-icon">👤</span>
              Profile
            </button>
            <button
              type="button"
              className={`tab-button ${activeTab === 'preferences' ? 'active' : ''}`}
              onClick={() => setActiveTab('preferences')}
            >
              <span className="tab-icon">⚙️</span>
              Preferences
            </button>
          </div>

          {activeTab === 'profile' && (
            <div className="tab-content">
              <Card className="settings-card">
                <h2>Profile Information</h2>
                <div style={{ display: 'flex', alignItems: 'center', gap: '16px', marginBottom: '16px' }}>
                  {avatar ? (
                    <img src={avatar} alt="Avatar" className="user-avatar" style={{ width: 60, height: 60, objectFit: 'cover' }} />
                  ) : (
                    <div className="user-avatar" aria-hidden="true" style={{ width: 60, height: 60 }}>
                      {(fullName || email || 'U').charAt(0).toUpperCase()}
                    </div>
                  )}
                  <div>
                    <div style={{ fontWeight: 600 }}>{fullName || 'Your Name'}</div>
                    <div className="muted" style={{ fontSize: '0.9rem' }}>{email || 'you@example.com'}</div>
                  </div>
                </div>

                <div style={{ display: 'grid', gap: '12px' }}>
                  <TextInput label="Full Name" value={fullName} onChange={(e) => setFullName(e.target.value)} />
                  <TextInput label="Email Address" value={email} readOnly />
                  <TextInput label="Role" value={role} readOnly />
                  <label className="field">
                    <span className="field-label">Avatar</span>
                    <input type="file" accept="image/*" onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (!file) return;
                      const reader = new FileReader();
                      reader.onload = () => setAvatar(reader.result);
                      reader.readAsDataURL(file);
                    }} />
                  </label>
                </div>

                <div style={{ marginTop: '14px' }}>
                  <PrimaryButton onClick={saveProfile}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M19 21H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h11l5 5v9a2 2 0 0 1-2 2z" />
                      <polyline points="17 21 17 13 7 13 7 21" />
                      <polyline points="7 3 7 8 15 8" />
                    </svg>
                    Save Profile
                  </PrimaryButton>
                </div>
              </Card>

              <Card className="settings-card" style={{ marginTop: '18px' }}>
                <h2>Security</h2>
                <p className="muted">Password changes require backend support. The form below is a placeholder.</p>
                {CP ? <CP /> : null}
              </Card>
            </div>
          )}

          {activeTab === 'preferences' && (
            <div className="tab-content">
              <Card className="settings-card" style={{ marginTop: '18px' }}>
                <h2>Default Page</h2>
                <p className="muted">The page you'll see first when logging in</p>
                <div style={{ marginTop: '8px' }}>
                  <select
                    className="field-input"
                    value={startingPage}
                    onChange={(e) => setStartingPage(e.target.value)}
                    style={{ borderRadius: '10px' }}
                  >
                    <option value="chat">Chat Interface</option>
                    <option value="docs">Developer Docs</option>
                    <option value="admin">Admin Dashboard</option>
                    <option value="history">Conversation History</option>
                  </select>
                </div>
              </Card>

              <Card className="settings-card" style={{ marginTop: '18px' }}>
                <h2>Notifications</h2>
                <p className="muted">Receive alerts for new messages and updates</p>
                <div style={{ marginTop: '8px' }}>
                  <button type="button" className={`toggle ${notificationsEnabled ? 'checked' : ''}`} onClick={() => setNotificationsEnabled((v) => !v)} aria-label="Toggle notifications">
                    <span className="toggle-track"></span>
                    <span className="toggle-thumb"></span>
                  </button>
                </div>

                <div style={{ marginTop: '16px' }}>
                  <PrimaryButton onClick={savePreferences}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <polyline points="20 6 9 17 4 12" />
                    </svg>
                    Save Preferences
                  </PrimaryButton>
                </div>
              </Card>

              <Card className="settings-card" style={{ marginTop: '18px' }}>
                <h2>Display</h2>
                <div style={{ display: 'grid', gap: '12px' }}>
                  <label className="field">
                    <span className="field-label">Text Size</span>
                    <select
                      className="field-input"
                      value={textSize}
                      onChange={(e) => {
                        const v = e.target.value;
                        setTextSize(v);
                        try { window.localStorage.setItem('cfc-text-size', v); } catch {}
                        const html = document.documentElement;
                        html.classList.toggle('text-size-small', v === 'small');
                        html.classList.toggle('text-size-medium', v === 'medium');
                        html.classList.toggle('text-size-large', v === 'large');
                      }}
                    >
                      <option value="small">Small</option>
                      <option value="medium">Medium</option>
                      <option value="large">Large</option>
                    </select>
                  </label>
                </div>
              </Card>

              
            </div>
          )}
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.SettingsPage = SettingsPage;
})();

// Local-only placeholder: non-functional Change Password modal
(() => {
  function scorePassword(pw) {
    if (!pw) return 0;
    let score = 0;
    const length = pw.length;
    score += Math.min(30, length * 3); // length
    if (/[a-z]/.test(pw)) score += 10;
    if (/[A-Z]/.test(pw)) score += 10;
    if (/\d/.test(pw)) score += 10;
    if (/[^A-Za-z0-9]/.test(pw)) score += 20;
    if (length >= 12) score += 20;
    return Math.max(0, Math.min(100, score));
  }

  function ChangePasswordPlaceholder() {
    const [open, setOpen] = React.useState(false);
    const [show, setShow] = React.useState({ current: false, next: false, confirm: false });
    const [fields, setFields] = React.useState({ current: '', next: '', confirm: '' });
    const strength = scorePassword(fields.next);
    const strong = strength >= 60;
    const match = fields.next && fields.next === fields.confirm;

    return (
      <div>
        <button type="button" className="btn-secondary" style={{ marginTop: '10px' }} onClick={() => setOpen(true)}>
          Change Password
        </button>

        {open && (
          <div className="modal-backdrop" onClick={() => setOpen(false)}>
            <div className="modal-body" onClick={(e) => e.stopPropagation()}>
              <button className="modal-close" type="button" onClick={() => setOpen(false)}>×</button>
              <div style={{ padding: '6px 8px' }}>
                <h3 style={{ marginTop: 0 }}>Change Password (Preview)</h3>
                <p className="muted" style={{ marginTop: 0 }}>This is a placeholder UI. Submitting requires server-side implementation.</p>

                <label className="field" style={{ marginTop: '10px' }}>
                  <span className="field-label">Current Password</span>
                  <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                    <input
                      type={show.current ? 'text' : 'password'}
                      className="field-input"
                      value={fields.current}
                      onChange={(e) => setFields((f) => ({ ...f, current: e.target.value }))}
                    />
                    <button type="button" className="btn-secondary" onClick={() => setShow((s) => ({ ...s, current: !s.current }))}> {show.current ? 'Hide' : 'Show'} </button>
                  </div>
                </label>

                <label className="field" style={{ marginTop: '10px' }}>
                  <span className="field-label">New Password</span>
                  <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                    <input
                      type={show.next ? 'text' : 'password'}
                      className="field-input"
                      value={fields.next}
                      onChange={(e) => setFields((f) => ({ ...f, next: e.target.value }))}
                    />
                    <button type="button" className="btn-secondary" onClick={() => setShow((s) => ({ ...s, next: !s.next }))}> {show.next ? 'Hide' : 'Show'} </button>
                  </div>
                </label>

                {fields.next ? (
                  <>
                    <div className="progress-bar small" style={{ marginTop: '8px' }} aria-label="New password strength">
                      <div className="progress-fill" style={{ width: `${strength}%` }} />
                    </div>
                    <p className="muted" style={{ marginTop: '6px' }}>{strong ? 'New password strength: strong' : 'Tip: add upper/lowercase, numbers, and symbols'}</p>
                  </>
                ) : null}

                <label className="field" style={{ marginTop: '10px' }}>
                  <span className="field-label">Confirm New Password</span>
                  <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                    <input
                      type={show.confirm ? 'text' : 'password'}
                      className="field-input"
                      value={fields.confirm}
                      onChange={(e) => setFields((f) => ({ ...f, confirm: e.target.value }))}
                    />
                    <button type="button" className="btn-secondary" onClick={() => setShow((s) => ({ ...s, confirm: !s.confirm }))}> {show.confirm ? 'Hide' : 'Show'} </button>
                  </div>
                </label>

                {fields.confirm && (
                  <p className="muted" style={{ marginTop: '8px' }}>{match ? 'Passwords match' : 'Passwords do not match yet'}</p>
                )}
                <div style={{ marginTop: '14px', display: 'flex', gap: '8px' }}>
                  <button type="button" className="btn-secondary" onClick={() => setOpen(false)}>Cancel</button>
                  <button type="button" className="btn-primary" disabled title="Requires backend implementation">
                    Save New Password
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.ChangePasswordPlaceholder = ChangePasswordPlaceholder;
})();
