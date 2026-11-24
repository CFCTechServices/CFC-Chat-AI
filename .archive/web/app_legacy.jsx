// React SPA for Chat-Talk UI
// Uses existing backend endpoints: /ask, /search, /files/upload, /api/videos/upload

// ---- Theme ----
const COLORS = {
  sand: '#D0A97C',
  green: '#379D63',
  blue: '#4065AE',
  text: '#111827',
  subtle: '#6B7280',
  border: '#E5E7EB',
  background: '#F3F4F6',
  white: '#FFFFFF',
};

// ---- User Context ----
const UserContext = React.createContext(null);

function useUser() {
  return React.useContext(UserContext);
}

function UserProvider({ children }) {
  const [user, setUser] = React.useState(() => {
    try {
      const raw = window.localStorage.getItem('cfc-user');
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  });

  const updateUser = React.useCallback((value) => {
    setUser(value);
    try {
      if (value) {
        window.localStorage.setItem('cfc-user', JSON.stringify(value));
      } else {
        window.localStorage.removeItem('cfc-user');
      }
    } catch {
      // ignore
    }
  }, []);

  return (
    <UserContext.Provider value={{ user, setUser: updateUser }}>
      {children}
    </UserContext.Provider>
  );
}

// ---- Theme Context ----
const ThemeContext = React.createContext(null);

function useTheme() {
  return React.useContext(ThemeContext);
}

function ThemeProvider({ children }) {
  const [isDark, setIsDark] = React.useState(() => {
    try {
      const saved = window.localStorage.getItem('cfc-theme');
      const shouldBeDark = saved === 'dark';
      // Set class immediately on initialization to prevent flash
      document.documentElement.classList.toggle('dark-mode', shouldBeDark);
      return shouldBeDark;
    } catch {
      return false;
    }
  });

  React.useEffect(() => {
    document.documentElement.classList.toggle('dark-mode', isDark);
    try {
      window.localStorage.setItem('cfc-theme', isDark ? 'dark' : 'light');
    } catch {
      // ignore
    }
  }, [isDark]);

  const toggleTheme = React.useCallback(() => {
    setIsDark((prev) => !prev);
  }, []);

  return (
    <ThemeContext.Provider value={{ isDark, toggleTheme }}>
      {children}
    </ThemeContext.Provider>
  );
}

// ---- Simple in-app router (no external dependency) ----
const RouterContext = React.createContext(null);

// Helper function to determine default route based on email
function getDefaultRouteForEmail(email) {
  if (!email) return 'login';
  const lower = email.toLowerCase();
  if (lower === 'admin@cfctech.com') return 'admin';
  if (lower === 'dev@cfctech.com') return 'docs';
  return 'chat';
}

function RouterProvider({ children }) {
  const { user } = useUser();
  
  // Initialize route from localStorage (synchronously)
  const getInitialRoute = () => {
    try {
      const savedRoute = window.localStorage.getItem('cfc-route');
      if (savedRoute && savedRoute !== 'transition') {
        return savedRoute;
      }
    } catch {
      // ignore
    }
    return 'login';
  };

  const [route, setRoute] = React.useState(getInitialRoute); // 'login' | 'docs' | 'admin' | 'chat' | 'transition'
  const [nextRoute, setNextRoute] = React.useState(null);
  const [visualState, setVisualState] = React.useState('idle'); // idle | fade-out | fade-in
  const hasInitialized = React.useRef(false);

  // Initialize or restore route on mount and when user changes
  React.useEffect(() => {
    if (!user) {
      // User logged out - clear route and go to login
      setRoute('login');
      try {
        window.localStorage.removeItem('cfc-route');
      } catch {
        // ignore
      }
      hasInitialized.current = false;
    } else {
      // User exists - check if we need to restore or set default route
      try {
        const savedRoute = window.localStorage.getItem('cfc-route');
        if (savedRoute && savedRoute !== 'transition') {
          // Restore saved route
          setRoute(savedRoute);
        } else if (!hasInitialized.current) {
          // First time with this user - calculate default based on email
          const defaultRoute = getDefaultRouteForEmail(user.email);
          setRoute(defaultRoute);
          window.localStorage.setItem('cfc-route', defaultRoute);
        }
      } catch {
        // ignore
      }
      hasInitialized.current = true;
    }
  }, [user]); // Only depend on user, not route to avoid loops

  const performNavigation = React.useCallback((next, options = {}) => {
    if (next === 'transition') {
      setNextRoute(options.to || null);
    } else {
      setNextRoute(null);
      // Save route to localStorage (except 'transition' which is temporary)
      try {
        window.localStorage.setItem('cfc-route', next);
      } catch {
        // ignore
      }
    }
    setRoute(next);
    requestAnimationFrame(() => setVisualState('fade-in'));
    setTimeout(() => setVisualState('idle'), 280);
  }, []);

  const navigate = React.useCallback((next, options = {}) => {
    if (options.withFade) {
      setVisualState('fade-out');
      setTimeout(() => performNavigation(next, options), 220);
    } else {
      performNavigation(next, options);
    }
  }, [performNavigation]);

  return (
    <RouterContext.Provider value={{ route, navigate, nextRoute, visualState }}>
      {children}
    </RouterContext.Provider>
  );
}

function useRouter() {
  return React.useContext(RouterContext);
}

// ---- Layout with logo and greeting ----
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

    // Only animate once when transitioning from login -> transition
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

    // For other route changes, just show full name without animating
    setGreetingName(target);
  }, [user?.name, route, hasAnimatedGreeting]);

  return (
    <div className="app-root">
      <header className="app-header">
        <div className="app-header-left">
          <img
            src="/ui/logo-cfc.png"
            alt="CFC Tech"
            className="app-logo"
          />
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
          <DarkModeToggle />
        </div>
      </header>

      <main className={`app-main page-fader ${visualState}`}>
        {children}
      </main>

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
    </div>
  );
}

// ---- Dark Mode Toggle ----
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

// ---- Shared UI primitives ----
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

// ---- Login Page ----
function LoginPage() {
  const [name, setName] = React.useState('');
  const [email, setEmail] = React.useState('');
  const [emailError, setEmailError] = React.useState('');
  const { setUser } = useUser();
  const { navigate } = useRouter();

  const validateEmail = (value) => {
    const trimmed = value.trim();
    const re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return re.test(trimmed);
  };

  const handleSubmit = (e) => {
    e.preventDefault();
    const trimmedEmail = email.trim();
    if (!validateEmail(trimmedEmail)) {
      setEmailError('Please enter a valid email address.');
      return;
    }
    setEmailError('');

    const rawName = name.trim();
    let displayName = 'Guest';
    if (rawName) {
      const parts = rawName.split(/\s+/);
      const first = parts[0];
      const capFirst =
        first.charAt(0).toUpperCase() + first.slice(1).toLowerCase();
      const rest = parts.slice(1).join(' ');
      displayName = rest ? `${capFirst} ${rest}` : capFirst;
    }

    const nextUser = {
      name: displayName,
      email: trimmedEmail,
    };
    setUser(nextUser);

    const lower = trimmedEmail.toLowerCase();
    let target = 'chat';
    if (lower === 'admin@cfctech.com') target = 'admin';
    else if (lower === 'dev@cfctech.com') target = 'docs';

    navigate('transition', { to: target, withFade: true });
  };

  return (
    <Layout>
      <div className="page login-page">
        <div className="login-hero">
          <h1>Welcome to CFC AI</h1>
          <p>
            A focused assistant for Concept5 and CFC knowledge.
            <br />
            Ask clear questions and get concise, guided answers in seconds.
          </p>
          <div className="login-badges">
            <span className="badge badge-sand">Built for your workflows</span>
            <span className="badge badge-green">Instant answers</span>
            <span className="badge badge-blue">Guided help</span>
          </div>
        </div>

        <Card className="login-card">
          <form onSubmit={handleSubmit} className="login-form">
            <h2>Sign in</h2>
            <TextInput
              label="Name"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Adam Smith"
              autoComplete="name"
            />
            <TextInput
              label="Email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="you@cfctech.com"
              autoComplete="email"
              error={emailError}
            />
            <PrimaryButton type="submit">Continue</PrimaryButton>
          </form>
        </Card>
      </div>
    </Layout>
  );
}

// ---- Developer / Docs Page ----
function DocsPage() {
  const { isDark } = useTheme();
  const iframeRef = React.useRef(null);

  // Inject dark mode CSS and apply theme
  React.useEffect(() => {
    const iframe = iframeRef.current;
    if (!iframe) return;

    const applyDarkMode = () => {
      try {
        const iframeDoc = iframe.contentDocument || iframe.contentWindow?.document;
        if (!iframeDoc) return;

        // Add or remove dark mode class on iframe's html element
        if (isDark) {
          iframeDoc.documentElement.classList.add('dark-mode');
        } else {
          iframeDoc.documentElement.classList.remove('dark-mode');
        }

        // Inject dark mode CSS if not already injected
        let styleEl = iframeDoc.getElementById('swagger-dark-mode-styles');
        if (!styleEl) {
          styleEl = iframeDoc.createElement('style');
          styleEl.id = 'swagger-dark-mode-styles';
          iframeDoc.head.appendChild(styleEl);
        }

        // Dark mode CSS for Swagger UI
        styleEl.textContent = `
          .dark-mode body {
            background: #1F2937 !important;
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui {
            background: #1F2937 !important;
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .topbar {
            background: #111827 !important;
            border-bottom: 1px solid #374151 !important;
          }
          .dark-mode .swagger-ui .info {
            background: #1F2937 !important;
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .info .title {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .info .description,
          .dark-mode .swagger-ui .info p,
          .dark-mode .swagger-ui .info .base-url {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .scheme-container {
            background: #1F2937 !important;
            border-color: #374151 !important;
          }
          .dark-mode .swagger-ui .scheme-container label {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .opblock {
            background: #374151 !important;
            border-color: #4B5563 !important;
          }
          .dark-mode .swagger-ui .opblock.opblock-get {
            border-color: #4065AE !important;
            background: rgba(64, 101, 174, 0.1) !important;
          }
          .dark-mode .swagger-ui .opblock.opblock-post {
            border-color: #379D63 !important;
            background: rgba(55, 157, 99, 0.1) !important;
          }
          .dark-mode .swagger-ui .opblock.opblock-put {
            border-color: #D0A97C !important;
            background: rgba(208, 169, 124, 0.1) !important;
          }
          .dark-mode .swagger-ui .opblock.opblock-delete {
            border-color: #EF4444 !important;
            background: rgba(239, 68, 68, 0.1) !important;
          }
          .dark-mode .swagger-ui .opblock-tag {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .opblock-tag small {
            color: #9CA3AF !important;
          }
          .dark-mode .swagger-ui .opblock-summary {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .opblock-summary-path,
          .dark-mode .swagger-ui .opblock-summary-path__deprecated {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .opblock-summary-description {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .opblock-description-wrapper p,
          .dark-mode .swagger-ui .opblock-description-wrapper code {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .opblock-description-wrapper,
          .dark-mode .swagger-ui .opblock-description {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .parameter__name,
          .dark-mode .swagger-ui .parameter__type {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .parameter__in,
          .dark-mode .swagger-ui .parameter__extension,
          .dark-mode .swagger-ui .parameter__deprecated {
            color: #9CA3AF !important;
          }
          .dark-mode .swagger-ui .response-col_status {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .response-col_description {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .response-col_links {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .model-box {
            background: #374151 !important;
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .model-title {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .prop-name {
            color: #9CA3AF !important;
          }
          .dark-mode .swagger-ui .prop-type {
            color: #60A5FA !important;
          }
          .dark-mode .swagger-ui table thead tr th {
            background: #374151 !important;
            color: #F9FAFB !important;
            border-color: #4B5563 !important;
          }
          .dark-mode .swagger-ui table tbody tr td {
            background: #1F2937 !important;
            color: #D1D5DB !important;
            border-color: #374151 !important;
          }
          .dark-mode .swagger-ui .btn {
            background: #4065AE !important;
            color: #FFFFFF !important;
            border-color: #4065AE !important;
          }
          .dark-mode .swagger-ui .btn:hover {
            background: #4A73C4 !important;
          }
          .dark-mode .swagger-ui input[type="text"],
          .dark-mode .swagger-ui input[type="email"],
          .dark-mode .swagger-ui input[type="password"],
          .dark-mode .swagger-ui textarea,
          .dark-mode .swagger-ui select {
            background: #374151 !important;
            color: #F9FAFB !important;
            border-color: #4B5563 !important;
          }
          .dark-mode .swagger-ui .highlight-code {
            background: #111827 !important;
          }
          .dark-mode .swagger-ui code {
            background: #111827 !important;
            color: #D1D5DB !important;
            border-color: #374151 !important;
          }
          .dark-mode .swagger-ui a {
            color: #60A5FA !important;
          }
          .dark-mode .swagger-ui a:hover {
            color: #93C5FD !important;
          }
          .dark-mode .swagger-ui .markdown p,
          .dark-mode .swagger-ui .markdown code,
          .dark-mode .swagger-ui .markdown pre {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .renderedMarkdown p {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui label {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .parameter__name.required {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .parameter__name.required::after {
            color: #EF4444 !important;
          }
          .dark-mode .swagger-ui .models {
            background: #1F2937 !important;
            border-color: #374151 !important;
          }
          .dark-mode .swagger-ui .models-control {
            background: #374151 !important;
            border-color: #4B5563 !important;
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .models-control label {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .model-container {
            background: #374151 !important;
            border-color: #4B5563 !important;
          }
          .dark-mode .swagger-ui .model-box {
            background: #374151 !important;
            border-color: #4B5563 !important;
          }
          .dark-mode .swagger-ui .model-toggle {
            background: #374151 !important;
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .model-toggle:hover {
            background: #4B5563 !important;
          }
          .dark-mode .swagger-ui .model-jump-to-path {
            color: #60A5FA !important;
          }
          .dark-mode .swagger-ui .model-title {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .model-title__text {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .prop {
            border-color: #4B5563 !important;
          }
          .dark-mode .swagger-ui .prop-name {
            color: #9CA3AF !important;
          }
          .dark-mode .swagger-ui .prop-type {
            color: #60A5FA !important;
          }
          .dark-mode .swagger-ui .prop-format {
            color: #9CA3AF !important;
          }
          .dark-mode .swagger-ui .prop-enum {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .model-box-control {
            color: #60A5FA !important;
          }
          .dark-mode .swagger-ui .model-box-control:hover {
            color: #93C5FD !important;
          }
          .dark-mode .swagger-ui .model-hint {
            color: #9CA3AF !important;
          }
          .dark-mode .swagger-ui .renderedMarkdown,
          .dark-mode .swagger-ui .renderedMarkdown p {
            color: #D1D5DB !important;
          }
          .dark-mode .swagger-ui .model-box-control,
          .dark-mode .swagger-ui .model-toggle {
            color: #60A5FA !important;
            text-decoration: none !important;
          }
          .dark-mode .swagger-ui .model-toggle::after {
            color: #60A5FA !important;
          }
          .dark-mode .swagger-ui .model-jump-to-path {
            color: #60A5FA !important;
          }
          .dark-mode .swagger-ui .model-jump-to-path:hover {
            color: #93C5FD !important;
          }
          .dark-mode .swagger-ui .opblock-summary-control {
            color: #F9FAFB !important;
          }
          .dark-mode .swagger-ui .opblock-summary-control path {
            fill: #F9FAFB !important;
          }
        `;
      } catch (err) {
        // Silently fail if iframe is not accessible (cross-origin or not loaded)
        console.debug('Could not apply dark mode to iframe:', err);
      }
    };

    // Apply immediately if iframe is already loaded
    if (iframe.contentDocument?.readyState === 'complete') {
      applyDarkMode();
    } else {
      // Wait for iframe to load
      iframe.addEventListener('load', applyDarkMode);
    }

    // Also apply when dark mode changes
    applyDarkMode();

    return () => {
      iframe.removeEventListener('load', applyDarkMode);
    };
  }, [isDark]);

  return (
    <Layout>
      <div className="page docs-page">
        <div className="page-header-row">
          <div>
            <h1>Developer Workspace</h1>
            <p>
              Explore and test the HTTP API alongside a focused view
              of your documentation.
            </p>
          </div>
        </div>

        <div className="docs-grid">
          <Card className="docs-card">
            <h2>Interactive API Docs</h2>
            <p className="muted">
              This is the same OpenAPI/Swagger documentation exposed at{' '}
              <code>/docs</code>, wrapped in a scrollable surface.
            </p>
            <div className="docs-iframe-wrapper">
              <iframe
                ref={iframeRef}
                src="/docs"
                title="API docs"
                className="docs-iframe"
              />
            </div>
          </Card>

          <Card className="docs-card notes-card">
            <h2>Implementation notes</h2>
            <ul className="notes-list">
              <br />
              <li>Search and chat endpoints backed by your RAG pipeline.</li>
              <br />
              <li>Video ingestion with Whisper and Pinecone indexing.</li>
              <br />
              <li>Admin uploads immediately trigger ingestion workflows.</li>
              <br />
              <li>Some elements of the interactive API docs may not display correctly in dark mode because of Swagger. Toggling dark mode on and off can sometimes fix this.</li>
            </ul>
          </Card>
        </div>
      </div>
    </Layout>
  );
}

// ---- Admin Page (upload + bulk upload, text vs video) ----
const VIDEO_EXTENSIONS = ['.mp4', '.mov', '.m4v', '.mkv', '.webm'];

function isVideoFile(file) {
  if (!file) return false;
  if (file.type && file.type.startsWith('video/')) return true;
  const name = file.name || '';
  const lower = name.toLowerCase();
  return VIDEO_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

function fileSlug(file) {
  const name = (file?.name || '').toLowerCase();
  return name.replace(/\.[^.]+$/, '').replace(/\s+/g, '-');
}

async function uploadSingleFile(file, onProgress) {
  if (!file) return { error: 'No file selected' };

  if (isVideoFile(file)) {
    // Video upload to /api/videos/upload
    const form = new FormData();
    form.append('slug', fileSlug(file));
    form.append('file', file);
    form.append('model', 'small');

    const res = await fetch('/api/videos/upload', {
      method: 'POST',
      body: form,
    });
    if (!res.ok) {
      const data = await res.json().catch(() => ({}));
      throw new Error(data.detail || 'Video upload failed');
    }
    if (onProgress) onProgress(100);
    return res.json();
  }

  // Document upload to /files/upload
  return new Promise((resolve, reject) => {
    const form = new FormData();
    form.append('file', file);
    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/files/upload');
    xhr.upload.addEventListener('progress', (e) => {
      if (onProgress && e.lengthComputable) {
        const pct = Math.round((e.loaded / e.total) * 100);
        onProgress(pct);
      }
    });
    xhr.onreadystatechange = () => {
      if (xhr.readyState === 4) {
        try {
          const data = JSON.parse(xhr.responseText || '{}');
          if (xhr.status >= 200 && xhr.status < 300) {
            if (onProgress) onProgress(100);
            resolve(data);
          } else {
            reject(new Error(data.error || data.detail || 'Upload failed'));
          }
        } catch (err) {
          reject(err);
        }
      }
    };
    xhr.send(form);
  });
}

function AdminPage() {
  const [singleFile, setSingleFile] = React.useState(null);
  const [singleStatus, setSingleStatus] = React.useState(null);
  const [singleProgress, setSingleProgress] = React.useState(0);

  const [bulkFiles, setBulkFiles] = React.useState([]);
  const [bulkItems, setBulkItems] = React.useState([]);
  const [bulkBusy, setBulkBusy] = React.useState(false);

  const handleSingleChange = (e) => {
    const file = e.target.files?.[0] || null;
    setSingleFile(file);
    setSingleStatus(null);
    setSingleProgress(0);
  };

  const handleSingleUpload = async () => {
    if (!singleFile) return;
    setSingleStatus({ state: 'uploading', message: 'Uploading…' });
    try {
      const data = await uploadSingleFile(singleFile, setSingleProgress);
      const isVideo = isVideoFile(singleFile);
      setSingleStatus({
        state: 'done',
        message: isVideo
          ? 'Video uploaded and transcribed.'
          : 'Document uploaded and ingested.',
        data,
      });
    } catch (err) {
      setSingleStatus({
        state: 'error',
        message: err.message || String(err),
      });
    }
  };

  const handleBulkChange = (e) => {
    const files = Array.from(e.target.files || []);
    setBulkFiles(files);
    setBulkItems(
      files.map((f) => ({
        id: `${f.name}-${Math.random().toString(36).slice(2)}`,
        file: f,
        progress: 0,
        status: 'ready',
        detail: '',
      })),
    );
  };

  const handleBulkUpload = async () => {
    if (!bulkFiles.length) return;
    setBulkBusy(true);
    const updated = [...bulkItems];

    const updateItem = (idx, patch) => {
      updated[idx] = { ...updated[idx], ...patch };
      setBulkItems([...updated]);
    };

    for (let i = 0; i < bulkFiles.length; i += 1) {
      const file = bulkFiles[i];
      updateItem(i, { status: 'uploading', detail: '' });
      try {
        const data = await uploadSingleFile(file, (pct) =>
          updateItem(i, { progress: pct }),
        );
        const isVideo = isVideoFile(file);
        updateItem(i, {
          status: 'done',
          progress: 100,
          detail: isVideo
            ? 'Video uploaded and transcribed.'
            : 'Document uploaded and ingested.',
          data,
        });
      } catch (err) {
        updateItem(i, {
          status: 'error',
          detail: err.message || String(err),
        });
      }
    }

    setBulkBusy(false);
  };

  return (
    <Layout>
      <div className="page admin-page">
        <div className="page-header-row">
          <div>
            <h1>Admin Console</h1>
            <p>
              Upload CFC documents and videos for ingestion. Files are routed to
              the correct pipeline automatically.
            </p>
          </div>
        </div>

        <div className="admin-grid">
          <Card className="admin-card">
            <h2>Upload</h2>
            <p className="muted">
              Upload a single document or video. We&apos;ll detect the file type
              and ingest it appropriately.
            </p>
            <div className="file-picker">
              <input
                type="file"
                onChange={handleSingleChange}
                className="file-input"
              />
              {singleFile && (
                <div className="file-chip">
                  <span>{singleFile.name}</span>
                  <span className="file-chip-kind">
                    {isVideoFile(singleFile) ? 'Video' : 'Document'}
                  </span>
                </div>
              )}
            </div>
            {singleProgress > 0 && (
              <div className="progress-bar">
                <div
                  className="progress-fill"
                  style={{ width: `${singleProgress}%` }}
                />
              </div>
            )}
            <div className="admin-actions">
              <PrimaryButton type="button" onClick={handleSingleUpload}>
                Upload &amp; ingest
              </PrimaryButton>
            </div>
            {singleStatus && (
              <div
                className={`status-pill status-${singleStatus.state || 'info'}`}
              >
                {singleStatus.message}
              </div>
            )}
          </Card>

          <Card className="admin-card">
            <h2>Bulk Upload</h2>
            <p className="muted">
              Select a collection of documents and videos. Each file is queued,
              uploaded, and ingested with progress tracking.
            </p>
            <div className="file-picker">
              <input
                type="file"
                multiple
                onChange={handleBulkChange}
                className="file-input"
              />
            </div>
            <div className="admin-actions">
              <PrimaryButton
                type="button"
                onClick={handleBulkUpload}
                disabled={bulkBusy || !bulkFiles.length}
              >
                {bulkBusy ? 'Uploading…' : 'Start bulk upload'}
              </PrimaryButton>
            </div>
            <div className="bulk-list">
              {bulkItems.map((item) => (
                <div key={item.id} className="bulk-item">
                  <div className="bulk-row">
                    <span className="bulk-name">{item.file.name}</span>
                    <span className={`bulk-status bulk-${item.status}`}>
                      {item.status === 'ready' && 'Ready'}
                      {item.status === 'uploading' && 'Uploading…'}
                      {item.status === 'done' && 'Ingested'}
                      {item.status === 'error' && 'Error'}
                    </span>
                  </div>
                  <div className="progress-bar small">
                    <div
                      className="progress-fill"
                      style={{ width: `${item.progress || 0}%` }}
                    />
                  </div>
                  {item.detail && (
                    <div className="bulk-detail">{item.detail}</div>
                  )}
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>
    </Layout>
  );
}

// ---- Chat Page ----
function formatTimecode(seconds) {
  if (seconds == null || Number.isNaN(seconds)) return null;
  const total = Math.max(0, Math.floor(seconds));
  const hrs = Math.floor(total / 3600);
  const mins = Math.floor((total % 3600) / 60);
  const secs = total % 60;
  const base = [hrs, mins, secs]
    .filter((v, idx) => v > 0 || idx > 0)
    .map((v) => String(v).padStart(2, '0'));
  return base.join(':');
}

function useModal() {
  const [content, setContent] = React.useState(null);
  const open = (c) => setContent(c);
  const close = () => setContent(null);

  const modal = content ? (
    <div className="modal-backdrop" onClick={close}>
      <div
        className="modal-body"
        onClick={(e) => e.stopPropagation()}
      >
        <button className="modal-close" type="button" onClick={close}>
          ×
        </button>
        {content}
      </div>
    </div>
  ) : null;

  return { open, close, modal };
}

function ChatMessage({ message, onImageClick, onVideoClick }) {
  const isUser = message.role === 'user';

  const escapeHtml = (text) =>
    text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');

  const renderMarkdown = React.useCallback((text) => {
    if (!text) return '';
    const escaped = escapeHtml(text);
    const lines = escaped.split(/\r?\n/);
    const blocks = [];
    let list = [];

    const flushList = () => {
      if (list.length) {
        blocks.push(`<ul>${list.map((item) => `<li>${item}</li>`).join('')}</ul>`);
        list = [];
      }
    };

    const formatInline = (value) =>
      value
        .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
        .replace(/\*(.+?)\*/g, '<em>$1</em>');

    lines.forEach((raw) => {
      const line = raw.trim();
      if (!line) {
        flushList();
        return;
      }
      const bullet = line.match(/^[-*]\s+(.*)/);
      if (bullet) {
        list.push(formatInline(bullet[1]));
        return;
      }
      flushList();
      blocks.push(`<p>${formatInline(line)}</p>`);
    });
    flushList();
    return blocks.join('\n');
  }, []);

  const MarkdownText = ({ text }) => {
    const html = React.useMemo(() => renderMarkdown(text), [text, renderMarkdown]);
    return <div className="chat-text markdown" dangerouslySetInnerHTML={{ __html: html }} />;
  };

  if (message.typing) {
    return (
      <div className="chat-message bot">
        <div className="chat-bubble typing-bubble">
          <div className="typing-dots-container">
            <span className="typing-dot" />
            <span className="typing-dot" />
            <span className="typing-dot" />
          </div>
          {message.showThinking && (
            <span className="typing-message">Assistant is thinking</span>
          )}
        </div>
      </div>
    );
  }

  return (
    <div className={`chat-message ${isUser ? 'user' : 'bot'}`}>
      <div className="chat-bubble">
        {message.segments && message.segments.length ? (
          message.segments.map((seg, idx) => {
            if (seg.type === 'text') {
              return <MarkdownText key={idx} text={seg.text} />;
            }
            if (seg.type === 'image') {
              return (
                <img
                  key={idx}
                  src={seg.url}
                  alt={seg.alt || 'Image'}
                  className="chat-image"
                  onClick={() => onImageClick && onImageClick(seg.url)}
                />
              );
            }
            if (seg.type === 'video') {
              return (
                <VideoBubble
                  key={idx}
                  segment={seg}
                  onVideoClick={onVideoClick}
                />
              );
            }
            return null;
          })
        ) : (
          <MarkdownText text={message.text} />
        )}
      </div>
    </div>
  );
}

function VideoBubble({ segment, onVideoClick }) {
  const videoRef = React.useRef(null);

  const handleSeek = (sec) => {
    if (videoRef.current) {
      videoRef.current.currentTime = sec;
      videoRef.current.play();
    }
  };

  const timestamps = segment.timestamps || [];

  return (
    <div className="video-bubble">
      <video
        ref={videoRef}
        className="chat-video"
        controls
        onClick={() =>
          onVideoClick &&
          onVideoClick({
            url: segment.url,
            timestamps,
          })
        }
      >
        <source src={segment.url} type="video/mp4" />
      </video>
      {timestamps.length > 0 && (
        <div className="video-timestamps">
          {timestamps.map((ts, idx) => (
            <button
              key={idx}
              type="button"
              className="timestamp-chip"
              onClick={() => handleSeek(ts.seconds)}
            >
              {ts.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function buildVideoSegmentsFromAnswer(data) {
  const url =
    data.answer_video_url ||
    (Array.isArray(data.video_context) &&
      data.video_context[0]?.video_url);
  if (!url) return [];

  const timestamps = [];
  const start = data.answer_start_seconds ?? data.video_context?.[0]?.start_seconds;
  const end = data.answer_end_seconds ?? data.video_context?.[0]?.end_seconds;

  if (start != null && end != null && end > start) {
    const span = end - start;
    const step = span / 3;
    const points = [start, start + step, start + 2 * step, end];
    points.slice(0, 4).forEach((sec) => {
      const label = formatTimecode(sec);
      if (label) {
        timestamps.push({ seconds: sec, label });
      }
    });
  } else if (Array.isArray(data.video_context)) {
    data.video_context.slice(0, 4).forEach((clip) => {
      const sec = clip.start_seconds ?? 0;
      const label =
        clip.timestamp || formatTimecode(sec) || 'Clip';
      timestamps.push({ seconds: sec, label });
    });
  }

  return [
    {
      type: 'video',
      url,
      timestamps,
    },
  ];
}

function buildImageSegmentsFromAnswer(data) {
  // Use relevant_images from backend if available (filtered and ranked)
  if (data.relevant_images && Array.isArray(data.relevant_images) && data.relevant_images.length > 0) {
    return data.relevant_images.map((img) => {
      const path = img.path || '';
      // Convert storage path to accessible URL
      const url = path.startsWith('http://') || path.startsWith('https://')
        ? path
        : `/content/images/${path}`;
      
      return {
        type: 'image',
        url,
        alt: img.alt_text || 'Document image',
        position: img.position, // Character position in text, or -1 for end
        path: path,
      };
    });
  }
  
  // Fallback: if no relevant_images, return empty (don't show all images)
  return [];
}

function ChatPage() {
  const [messages, setMessages] = React.useState([]);
  const [input, setInput] = React.useState('');
  const [sending, setSending] = React.useState(false);
  const [attachedImages, setAttachedImages] = React.useState([]);
  const chatThreadRef = React.useRef(null);
  const thinkingTimeoutsRef = React.useRef({});

  const { open: openModal, modal } = useModal();

  // Prepare conversation history using "first + recent" strategy
  const prepareConversationHistory = (messages, maxMessages = 8) => {
    // Filter to only user and assistant messages
    const filtered = messages.filter(m => m.role === 'user' || m.role === 'assistant');
    
    if (filtered.length <= maxMessages) {
      return filtered;
    }
    
    // Get first 2-3 messages (conversation anchor)
    const firstCount = Math.min(3, Math.floor(filtered.length / 3));
    const firstMessages = filtered.slice(0, firstCount);
    
    // Get most recent messages (immediate context)
    const recentCount = maxMessages - firstCount;
    const recentMessages = filtered.slice(-recentCount);
    
    // Combine and deduplicate by message ID
    const combined = [...firstMessages];
    const firstIds = new Set(firstMessages.map(m => m.id));
    
    for (const msg of recentMessages) {
      if (!firstIds.has(msg.id)) {
        combined.push(msg);
      }
    }
    
    return combined;
  };

  // Auto-scroll to bottom when messages change
  React.useEffect(() => {
    if (chatThreadRef.current) {
      chatThreadRef.current.scrollTop = chatThreadRef.current.scrollHeight;
    }
  }, [messages]);

  // Show "Assistant is thinking" message after 7 seconds for typing messages
  React.useEffect(() => {
    messages.forEach((msg) => {
      if (msg.typing && !msg.showThinking) {
        // Set a timeout to show thinking message after 3 seconds
        if (!thinkingTimeoutsRef.current[msg.id]) {
          thinkingTimeoutsRef.current[msg.id] = setTimeout(() => {
            setMessages((prev) => {
              // Only show thinking message if message is still typing
              const currentMsg = prev.find((m) => m.id === msg.id);
              if (currentMsg && currentMsg.typing) {
                return prev.map((m) =>
                  m.id === msg.id ? { ...m, showThinking: true } : m
                );
              }
              return prev;
            });
          }, 7000);
        }
      } else if (!msg.typing) {
        // Clear timeout and remove thinking message when typing stops
        if (thinkingTimeoutsRef.current[msg.id]) {
          clearTimeout(thinkingTimeoutsRef.current[msg.id]);
          delete thinkingTimeoutsRef.current[msg.id];
        }
        if (msg.showThinking) {
          setMessages((prev) =>
            prev.map((m) =>
              m.id === msg.id ? { ...m, showThinking: false } : m
            )
          );
        }
      }
    });

    // Cleanup function to clear timeouts when component unmounts
    return () => {
      Object.values(thinkingTimeoutsRef.current).forEach((timeout) => {
        if (timeout) clearTimeout(timeout);
      });
      thinkingTimeoutsRef.current = {};
    };
  }, [messages]);

  const handleImageChange = (e) => {
    const files = Array.from(e.target.files || []);
    const previews = files.map((file) => ({
      file,
      url: URL.createObjectURL(file),
      id: `${file.name}-${Math.random().toString(36).slice(2)}`,
    }));
    setAttachedImages((prev) => [...prev, ...previews]);
  };

  const appendMessage = (msg) => {
    setMessages((prev) => [...prev, msg]);
  };

  const simulateStreaming = (fullText, baseMessageId, extraSegments = []) => {
    const chars = Array.from(fullText);
    let idx = 0;
    const speed = 18;

    // Separate images with positions from other segments (videos, etc.)
    const imageSegments = extraSegments.filter((seg) => seg.type === 'image');
    const otherSegments = extraSegments.filter((seg) => seg.type !== 'image');
    
    // Sort images by position (ascending), -1 (end) goes last
    const sortedImages = [...imageSegments].sort((a, b) => {
      const posA = a.position ?? -1;
      const posB = b.position ?? -1;
      if (posA === -1) return 1; // -1 goes to end
      if (posB === -1) return -1;
      return posA - posB;
    });

    const buildSegments = (textLength) => {
      const segments = [];
      let textStart = 0;
      const insertedImages = new Set(); // Track which images we've inserted
      
      // Process images that should appear within the current text
      for (const img of sortedImages) {
        const imgPos = img.position ?? -1;
        
        // Skip images at end or beyond current text length (will add later)
        if (imgPos === -1 || imgPos >= textLength) {
          continue;
        }
        
        // Add text before image
        if (imgPos > textStart) {
          const textBefore = fullText.slice(textStart, imgPos);
          if (textBefore.trim()) {
            segments.push({ type: 'text', text: textBefore });
          }
        }
        
        // Add image
        segments.push(img);
        insertedImages.add(img.path || img.url);
        textStart = imgPos;
      }
      
      // Add remaining text up to current length
      if (textStart < textLength) {
        const remainingText = fullText.slice(textStart, textLength);
        if (remainingText.trim()) {
          segments.push({ type: 'text', text: remainingText });
        }
      }
      
      // Add images that should appear at end (position -1 or beyond current text)
      for (const img of sortedImages) {
        if (!insertedImages.has(img.path || img.url)) {
          segments.push(img);
        }
      }
      
      // Add other segments (videos) at the end
      segments.push(...otherSegments);
      
      return segments;
    };

    const interval = setInterval(() => {
      idx += 3;
      const currentLength = Math.min(idx, chars.length);
      const segments = buildSegments(currentLength);
      
      setMessages((prev) =>
        prev.map((m) =>
          m.id === baseMessageId
            ? {
                ...m,
                typing: false,
                text: fullText.slice(0, currentLength),
                segments: segments,
              }
            : m,
        ),
      );
      // Scroll to bottom during streaming
      if (chatThreadRef.current) {
        chatThreadRef.current.scrollTop = chatThreadRef.current.scrollHeight;
      }
      if (idx >= chars.length) {
        clearInterval(interval);
      }
    }, speed);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    const q = input.trim();
    if (!q || sending) return;

    const imageSegments = attachedImages.map((img) => ({
      type: 'image',
      url: img.url,
      alt: 'Attached image',
    }));

    const userMessage = {
      id: `u-${Date.now()}`,
      role: 'user',
      text: q,
      segments: [{ type: 'text', text: q }, ...imageSegments],
    };
    appendMessage(userMessage);

    const imagesForBackend = attachedImages.map((img) => img.url);

    setInput('');
    setAttachedImages([]);
    setSending(true);

    const botId = `b-${Date.now()}`;
    appendMessage({
      id: botId,
      role: 'assistant',
      text: '',
      segments: [],
      typing: true,
    });

    try {
      // Prepare conversation history using "first + recent" strategy
      const conversationHistory = prepareConversationHistory(
        messages.filter(m => m.role === 'user' || m.role === 'assistant')
      ).map(m => ({
        role: m.role,
        content: m.text
      }));
      
      const res = await fetch('/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          question: q + (imagesForBackend.length ? ` (images: ${imagesForBackend.join(', ')})` : ''),
          top_k: 4,
          conversation_history: conversationHistory.length > 0 ? conversationHistory : undefined,
        }),
      });
      const data = await res.json();
      if (!data.success) {
        throw new Error(data.detail || 'Error from assistant');
      }

      const answer = data.answer || 'No answer available.';
      const videoSegments = buildVideoSegmentsFromAnswer(data);
      const imageSegments = buildImageSegmentsFromAnswer(data);
      // Combine video and image segments, images first so they appear above text
      const allSegments = [...imageSegments, ...videoSegments];
      simulateStreaming(answer, botId, allSegments);
    } catch (err) {
      setMessages((prev) =>
        prev.map((m) =>
          m.id === botId
            ? {
                ...m,
                typing: false,
                text: err.message || String(err),
                segments: [{ type: 'text', text: err.message || String(err) }],
              }
            : m,
        ),
      );
    } finally {
      setSending(false);
    }
  };

  const handleImageClick = (url) => {
    openModal(
      <img src={url} alt="Preview" className="modal-image" />,
    );
  };

  const handleVideoClick = ({ url, timestamps }) => {
    openModal(
      <div className="modal-video-wrapper">
        <video src={url} className="modal-video" controls autoPlay />
        {timestamps && timestamps.length > 0 && (
          <div className="video-timestamps">
            {timestamps.map((ts, idx) => (
              <span key={idx} className="timestamp-chip static">
                {ts.label}
              </span>
            ))}
          </div>
        )}
      </div>,
    );
  };

  return (
    <Layout>
      <div className="page chat-page">
        <div className="page-header-row">
          <div>
            <h1>Chat with CFC AI</h1>
            <p>
              Ask questions about your software and get quick, informed answers.
            </p>
          </div>
        </div>

        <Card className="chat-card">
          <div className="chat-thread" id="chatThread" ref={chatThreadRef}>
            {messages.map((m) => (
              <ChatMessage
                key={m.id}
                message={m}
                onImageClick={handleImageClick}
                onVideoClick={handleVideoClick}
              />
            ))}
          </div>
          <form className="chat-composer" onSubmit={handleSubmit}>
            <div className="composer-row">
              <button
                type="button"
                className={`composer-info-icon ${messages.length > 12 ? 'visible' : ''}`}
                onClick={() => openModal(
                  <div className="conversation-info-modal">
                    <h3>Long Conversation Notice</h3>
                    <p>
                      This conversation is getting long. CFC AI may begin to lose sight of the original goal in very long conversations.
                    </p>
                    <p>
                      For separate questions or new topics, consider refreshing the page to start a new conversation. This ensures the most reliable and focused responses.
                    </p>
                  </div>
                )}
                title="Conversation length info"
                aria-label="Conversation length information"
              >
                <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
                  <circle cx="10" cy="10" r="9" stroke="currentColor" strokeWidth="1.5" fill="none"/>
                  <path d="M10 7V10" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round"/>
                  <circle cx="10" cy="13" r="1" fill="currentColor"/>
                </svg>
              </button>
              <input
                type="text"
                className="composer-input"
                placeholder="Ask anything about CFC software…"
                value={input}
                onChange={(e) => setInput(e.target.value)}
              />
              <label className="btn-primary composer-button" title="Attach images">
                <span>Attach</span>
                <input
                  type="file"
                  accept="image/*"
                  multiple
                  onChange={handleImageChange}
                />
              </label>
              <button
                type="submit"
                className="btn-primary composer-button"
                disabled={sending}
              >
                {sending ? 'Sending…' : 'Send'}
              </button>
            </div>
            {attachedImages.length > 0 && (
              <div className="attached-images">
                {attachedImages.map((img) => (
                  <img
                    key={img.id}
                    src={img.url}
                    alt="Attachment"
                    className="attached-thumb"
                    onClick={() => handleImageClick(img.url)}
                  />
                ))}
              </div>
            )}
          </form>
        </Card>
      </div>
      {modal}
    </Layout>
  );
}

// ---- App & routing ----
function App() {
  return (
    <ThemeProvider>
      <UserProvider>
        <RouterProvider>
          <AppRoutes />
        </RouterProvider>
      </UserProvider>
    </ThemeProvider>
  );
}

function AppRoutes() {
  const { route } = useRouter();

  if (route === 'transition') return <TransitionPage />;
  if (route === 'docs') return <DocsPage />;
  if (route === 'admin') return <AdminPage />;
  if (route === 'chat') return <ChatPage />;

  return <LoginPage />;
}

function TransitionPage() {
  const { user } = useUser();
  const { nextRoute, navigate } = useRouter();

  React.useEffect(() => {
    const id = setTimeout(() => {
      navigate(nextRoute || 'login', { withFade: true });
    }, 1800);
    return () => clearTimeout(id);
  }, [nextRoute, navigate]);

  const firstName = (user?.name || 'there').split(' ')[0];

  return (
    <Layout>
      <div className="page transition-screen">
        <div className="transition-inner standalone">
          <h1 className="transition-title">Welcome, {firstName}!</h1>
          <p className="muted">
            We&apos;re getting everything ready for you.
            <br />
            This will only take a moment.
          </p>
          <div className="transition-pill">Preparing your workspace</div>
          <div className="transition-loader">
            <div className="dot" />
            <div className="dot" />
            <div className="dot" />
          </div>
        </div>
      </div>
    </Layout>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);


