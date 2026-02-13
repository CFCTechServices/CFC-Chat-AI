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
    const { user } = useUser();
    const { route, navigate, visualState } = useRouter();

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
              <button
                type="button"
                className="link-button"
                onClick={() => navigate('login')}
              >
                Return to login
              </button>
            )}
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
                  <circle cx="12" cy="12" r="3" />
                  <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09a1.65 1.65 0 0 0 1.51-1 1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33h0A1.65 1.65 0 0 0 9 3.09V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51h0a1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82h0A1.65 1.65 0 0 0 20.91 11H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z" />
                </svg>
              </button>
            </nav>
            {user?.name ? (
              <span className="app-greeting">
                {greetingName ? `Hi, ${greetingName}!` : 'Hi,'}
              </span>
            ) : (
              <span className="app-greeting muted">Hi there!</span>
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
                onClick={() => navigate('login')}
              >
                Return to login
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
