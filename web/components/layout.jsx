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

  function Layout({ children }) {
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
                onClick={() => navigate('login')}
              >
                Return to login
              </button>
            )}
            <DarkModeToggle />
          </div>
        </header>

        <main className={`app-main page-fader ${visualState}`}>
          {children}
        </main>

        <footer className="app-footer">
        </footer>
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Layout = { Layout, DarkModeToggle };
})();
