// App layout shell (header/footer) and dark mode toggle
(() => {
  const { useTheme } = window.CFC.ThemeContext;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;

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
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
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
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
              </svg>
            )}
          </span>
        </span>
      </button>
    );
  }

  function Layout({ children, fullWidth }) {
    const { user, role, supabase } = useUser();
    const { route, navigate, visualState } = useRouter();

    const handleSignOut = async () => {
      const client = supabase || window.supabaseClient;
      if (client) {
        await client.auth.signOut();
      }
      navigate('login');
    };

    const showBackToLogin = route !== 'login' && route !== 'transition';
    const [greetingName, setGreetingName] = React.useState('');
    const [hasAnimatedGreeting, setHasAnimatedGreeting] = React.useState(false);

    React.useEffect(() => {
      if (!user?.name) {
        setGreetingName('');
        setHasAnimatedGreeting(false);
        return;
      }
      const target = (user.name.split(' ')[0] || '').toString();

      if (route === 'transition' && !hasAnimatedGreeting) {
        let idx = 0;
        setGreetingName('');
        const interval = setInterval(() => {
          idx += 1;
          setGreetingName(target.slice(0, idx));
          if (idx >= target.length) {
            clearInterval(interval);
            setHasAnimatedGreeting(true);
          }
        }, 140);
        return () => clearInterval(interval);
      }

      setGreetingName(target);
    }, [user?.name, route, hasAnimatedGreeting]);

    return (
      <div className="app-root">
        <header className="app-header">
          <div className="app-header-left">
            <img src="/ui/logo-cfc.png" alt="CFC Tech" className="app-logo" />
            <div className="app-header-brand">
              <div className="app-title">Support Assistant</div>
              <div className="app-subtitle">Conversational help for CFC Technologies</div>
            </div>
          </div>
          <div className="app-header-right">
            {showBackToLogin && (
              <React.Fragment>
                <nav className="toolbar" aria-label="Primary navigation">
              <button
                type="button"
                className={`toolbar-btn ${route === 'chat' ? 'active' : ''}`}
                onClick={() => navigate('chat', { withFade: true })}
                title="Chat"
                aria-label="Chat"
              >
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M21 15a4 4 0 0 1-4 4H7l-4 4V5a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4z" />
                </svg>
              </button>
              <button
                type="button"
                className={`toolbar-btn ${route === 'history' ? 'active' : ''}`}
                onClick={() => navigate('history', { withFade: true })}
                title="History"
                aria-label="History"
              >
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <polyline points="1 4 1 10 7 10" />
                  <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
                  <polyline points="12 7 12 12 16 14" />
                </svg>
              </button>
              <button
                type="button"
                className={`toolbar-btn ${route === 'settings' ? 'active' : ''}`}
                onClick={() => navigate('settings', { withFade: true })}
                title="Settings"
                aria-label="Settings"
              >
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" />
                  <circle cx="12" cy="12" r="3" />
                </svg>
              </button>
              {role === 'admin' && (
                <button
                  type="button"
                  className={`toolbar-btn ${route === 'admin' ? 'active' : ''}`}
                  onClick={() => navigate('admin', { withFade: true })}
                  title="Admin"
                  aria-label="Admin"
                >
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M12 2L2 7l10 5 10-5-10-5z" />
                    <path d="M2 17l10 5 10-5" />
                    <path d="M2 12l10 5 10-5" />
                  </svg>
                </button>
              )}
            </nav>
            {user?.name ? (
              <span className="app-greeting">
                {greetingName ? `Hi, ${greetingName}!` : 'Hi,'}
              </span>
            ) : (
              <span className="app-greeting muted">Hi there!</span>
            )}
            {showBackToLogin && (
              <button
                type="button"
                className="link-button"
                onClick={handleSignOut}
              >
                Sign Out
              </button>
            )}
              </React.Fragment>
            )}
            <DarkModeToggle />
          </div>
        </header>

        <main className={`app-main ${fullWidth ? 'app-main--full' : ''} page-fader ${visualState}`}>
          {children}
        </main>

        {!fullWidth && (
          <footer className="app-footer">
            {showBackToLogin && (
              <button
                type="button"
                className="link-button"
                onClick={handleSignOut}
              >
                Sign Out
              </button>
            )}
          </footer>
        )}
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Layout = { Layout, DarkModeToggle };
})();
