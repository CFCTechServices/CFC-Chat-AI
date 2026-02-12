// App layout shell (header/footer) with profile management
(() => {
  const { useTheme } = window.CFC.ThemeContext;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;
  const { useState, useEffect, useRef, useCallback } = React;

  function DarkModeToggle() {
    const { isDark, toggleTheme } = useTheme();

    return (
      <button
        type="button"
        className={`theme-toggle ${isDark ? 'dark' : 'light'}`}
        onClick={toggleTheme}
        aria-label={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
        title={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
      >
        <span className="theme-toggle-track">
          <span className="theme-toggle-thumb">
            {isDark ? (
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="5" />
                <line x1="12" y1="1" x2="12" y2="3" />
                <line x1="12" y1="21" x2="12" y2="23" />
                <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
                <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
                <line x1="1" y1="12" x2="3" y2="12" />
                <line x1="21" y1="12" x2="23" y2="12" />
                <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
                <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
              </svg>
            ) : (
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
              </svg>
            )}
          </span>
        </span>
      </button>
    );
  }

  function ProfileDropdown() {
    const { user, session, profile, setProfile } = useUser();
    const [open, setOpen] = useState(false);
    const [editing, setEditing] = useState(false);
    const [fullName, setFullName] = useState('');
    const [saving, setSaving] = useState(false);
    const [profileData, setProfileData] = useState(null);
    const dropdownRef = useRef(null);

    useEffect(() => {
      if (open && session && !profileData) {
        fetch('/api/profile/me', {
          headers: { 'Authorization': `Bearer ${session.access_token}` },
        })
          .then(res => res.ok ? res.json() : null)
          .then(data => {
            if (data) {
              setProfileData(data);
              setFullName(data.full_name || '');
            }
          })
          .catch(() => {});
      }
    }, [open, session, profileData]);

    useEffect(() => {
      const handleClickOutside = (e) => {
        if (dropdownRef.current && !dropdownRef.current.contains(e.target)) {
          setOpen(false);
          setEditing(false);
        }
      };
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    const handleSave = async () => {
      if (!session) return;
      setSaving(true);
      try {
        const res = await fetch('/api/profile/me', {
          method: 'PATCH',
          headers: {
            'Authorization': `Bearer ${session.access_token}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ full_name: fullName }),
        });
        if (res.ok) {
          const data = await res.json();
          setProfileData(data);
          setEditing(false);
          if (setProfile) setProfile({ ...profile, full_name: data.full_name });
        }
      } catch (e) {
        console.error('Failed to update profile:', e);
      } finally {
        setSaving(false);
      }
    };

    const displayName = profileData?.full_name || profile?.full_name || user?.email?.split('@')[0] || '';

    return (
      <div ref={dropdownRef} style={{ position: 'relative' }}>
        <button
          className="link-button"
          onClick={() => setOpen(!open)}
          style={{ fontSize: '0.95rem', cursor: 'pointer' }}
          aria-haspopup="true"
          aria-expanded={open}
          aria-label="Profile menu"
        >
          {displayName ? `Hi, ${displayName}!` : 'Hi!'}
        </button>

        {open && (
          <div style={{
            position: 'absolute',
            top: '100%',
            right: 0,
            marginTop: '8px',
            backgroundColor: 'var(--color-surface, white)',
            border: '1px solid var(--color-border, #e5e7eb)',
            borderRadius: '10px',
            padding: '16px',
            minWidth: '260px',
            boxShadow: '0 4px 12px rgba(0,0,0,0.1)',
            zIndex: 100,
          }}>
            <div style={{ marginBottom: '12px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
              {user?.email}
            </div>

            {profileData && (
              <div style={{ marginBottom: '12px' }}>
                <div style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Role</div>
                <div style={{ fontWeight: 600 }}>{profileData.role}</div>
              </div>
            )}

            {!editing ? (
              <div>
                <div style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Name</div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span>{profileData?.full_name || 'Not set'}</span>
                  <button
                    className="link-button"
                    onClick={() => setEditing(true)}
                    style={{ fontSize: '0.8rem' }}
                  >
                    Edit
                  </button>
                </div>
              </div>
            ) : (
              <div>
                <div style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Name</div>
                <div style={{ display: 'flex', gap: '8px' }}>
                  <input
                    type="text"
                    value={fullName}
                    onChange={(e) => setFullName(e.target.value)}
                    placeholder="Your full name"
                    style={{
                      flex: 1,
                      padding: '4px 8px',
                      borderRadius: '6px',
                      border: '1px solid var(--color-border)',
                      fontSize: '0.9rem',
                      backgroundColor: 'var(--color-surface)',
                      color: 'var(--color-text)',
                    }}
                  />
                  <button className="btn-primary" onClick={handleSave} disabled={saving} style={{ padding: '4px 10px', fontSize: '0.8rem' }}>
                    {saving ? '...' : 'Save'}
                  </button>
                  <button className="btn-secondary" onClick={() => setEditing(false)} style={{ padding: '4px 10px', fontSize: '0.8rem' }}>
                    Cancel
                  </button>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    );
  }

  function Layout({ children }) {
    const { user, role, supabase } = useUser();
    const { route, navigate, visualState } = useRouter();

    return (
      <div className="app-root">
        <header className="app-header">
          <div className="app-header-left">
            <img src="/ui/logo-cfc.png" alt="CFC Tech" className="app-logo" />
            <div className="app-header-brand">
              <div className="app-title">Support Assistant</div>
              <div className="app-subtitle">Conversational help for CFC Technologies</div>
            </div>

            {/* Admin Navigation */}
            {user && role === 'admin' && (
              <div style={{ marginLeft: '40px', display: 'flex', gap: '10px' }}>
                <button
                  className={route === 'chat' ? 'btn-primary' : 'btn-secondary'}
                  onClick={() => navigate('chat')}
                  style={{ padding: '4px 12px', fontSize: '0.9rem' }}
                >
                  Chat
                </button>
                <button
                  className={route === 'admin' ? 'btn-primary' : 'btn-secondary'}
                  onClick={() => navigate('admin')}
                  style={{ padding: '4px 12px', fontSize: '0.9rem' }}
                >
                  Admin
                </button>
              </div>
            )}
          </div>
          <div className="app-header-right">
            {user?.email ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
                <ProfileDropdown />
                <button
                  className="link-button"
                  onClick={() => supabase?.auth?.signOut()}
                  style={{ fontSize: '0.9rem' }}
                >
                  Sign Out
                </button>
              </div>
            ) : (
              <span className="app-greeting muted">Hi there!</span>
            )}
            <DarkModeToggle />
          </div>
        </header>

        <main className={`app-main page-fader ${visualState}`}>
          {children}
        </main>

        <footer className="app-footer">
          &copy; 2026 CFC Tech Services
        </footer>
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Layout = { Layout, DarkModeToggle };
})();
