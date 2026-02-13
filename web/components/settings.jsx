// User Settings page with Profile and Preferences tabs
(() => {
  const { Layout } = window.CFC.Layout;
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { useUser } = window.CFC.UserContext;

  function SettingsPage() {
    const { user, session, profile, setProfile, supabase } = useUser();

    const [activeTab, setActiveTab] = React.useState('profile'); // 'profile' | 'preferences'
    const [saving, setSaving] = React.useState(false);
    const [saveMsg, setSaveMsg] = React.useState('');

    // Profile state — initialize from profile data fetched via Supabase
    const [fullName, setFullName] = React.useState(profile?.full_name || user?.name || '');
    const [email] = React.useState(user?.email || '');
    const [role] = React.useState(profile?.role || 'user');
    const [avatar, setAvatar] = React.useState(profile?.avatar_url || '');

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


    const saveProfile = async () => {
      setSaving(true);
      setSaveMsg('');
      try {
        const token = session?.access_token;
        if (!token) throw new Error('Not authenticated');
        const body = {};
        if (fullName) body.full_name = fullName;
        if (avatar) body.avatar_url = avatar;

        const res = await fetch('/api/profile/me', {
          method: 'PATCH',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`,
          },
          body: JSON.stringify(body),
        });
        if (!res.ok) {
          const err = await res.json().catch(() => ({}));
          throw new Error(err.detail || 'Failed to save profile');
        }
        const updated = await res.json();
        setProfile(updated);
        setSaveMsg('Profile saved successfully.');
      } catch (err) {
        setSaveMsg(err.message || 'Failed to save profile.');
      } finally {
        setSaving(false);
      }
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

                {saveMsg && (
                  <div style={{ marginTop: '10px', padding: '8px 12px', borderRadius: '8px', fontSize: '0.9rem',
                    backgroundColor: saveMsg.includes('success') ? 'var(--color-info-bg, #eff6ff)' : 'var(--color-error-bg, #fef2f2)',
                    color: saveMsg.includes('success') ? 'var(--color-info, #2563eb)' : 'var(--color-error, #dc2626)' }}>
                    {saveMsg}
                  </div>
                )}
                <div style={{ marginTop: '14px' }}>
                  <PrimaryButton onClick={saveProfile} disabled={saving}>
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M19 21H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h11l5 5v9a2 2 0 0 1-2 2z" />
                      <polyline points="17 21 17 13 7 13 7 21" />
                      <polyline points="7 3 7 8 15 8" />
                    </svg>
                    {saving ? 'Saving...' : 'Save Profile'}
                  </PrimaryButton>
                </div>
              </Card>

              <Card className="settings-card" style={{ marginTop: '18px' }}>
                <h2>Security</h2>
                <p className="muted">Update your account password.</p>
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

// Change Password modal — uses Supabase auth.updateUser
(() => {
  const { useUser } = window.CFC.UserContext;

  function scorePassword(pw) {
    if (!pw) return 0;
    let score = 0;
    const length = pw.length;
    score += Math.min(30, length * 3);
    if (/[a-z]/.test(pw)) score += 10;
    if (/[A-Z]/.test(pw)) score += 10;
    if (/\d/.test(pw)) score += 10;
    if (/[^A-Za-z0-9]/.test(pw)) score += 20;
    if (length >= 12) score += 20;
    return Math.max(0, Math.min(100, score));
  }

  function ChangePasswordPlaceholder() {
    const { supabase } = useUser();
    const [open, setOpen] = React.useState(false);
    const [show, setShow] = React.useState({ next: false, confirm: false });
    const [fields, setFields] = React.useState({ next: '', confirm: '' });
    const [saving, setSaving] = React.useState(false);
    const [msg, setMsg] = React.useState('');
    const strength = scorePassword(fields.next);
    const strong = strength >= 60;
    const match = fields.next && fields.next === fields.confirm;

    const handleSave = async () => {
      setMsg('');
      if (fields.next.length < 6) {
        setMsg('Password must be at least 6 characters.');
        return;
      }
      if (!match) {
        setMsg('Passwords do not match.');
        return;
      }
      setSaving(true);
      try {
        const client = supabase || window.supabaseClient;
        const { error } = await client.auth.updateUser({ password: fields.next });
        if (error) throw error;
        setMsg('Password updated successfully!');
        setFields({ next: '', confirm: '' });
        setTimeout(() => setOpen(false), 1500);
      } catch (err) {
        setMsg(err.message || 'Failed to update password.');
      } finally {
        setSaving(false);
      }
    };

    return (
      <div>
        <button type="button" className="btn-secondary" style={{ marginTop: '10px' }} onClick={() => { setOpen(true); setMsg(''); }}>
          Change Password
        </button>

        {open && (
          <div className="modal-backdrop" onClick={() => setOpen(false)}>
            <div className="modal-body" onClick={(e) => e.stopPropagation()}>
              <button className="modal-close" type="button" onClick={() => setOpen(false)}>x</button>
              <div style={{ padding: '6px 8px' }}>
                <h3 style={{ marginTop: 0 }}>Change Password</h3>

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

                {msg && (
                  <div style={{ marginTop: '10px', padding: '8px 12px', borderRadius: '8px', fontSize: '0.9rem',
                    backgroundColor: msg.includes('success') ? 'var(--color-info-bg, #eff6ff)' : 'var(--color-error-bg, #fef2f2)',
                    color: msg.includes('success') ? 'var(--color-info, #2563eb)' : 'var(--color-error, #dc2626)' }}>
                    {msg}
                  </div>
                )}

                <div style={{ marginTop: '14px', display: 'flex', gap: '8px' }}>
                  <button type="button" className="btn-secondary" onClick={() => setOpen(false)}>Cancel</button>
                  <button type="button" className="btn-primary" disabled={saving || !match || !strong} onClick={handleSave}>
                    {saving ? 'Saving...' : 'Save New Password'}
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
