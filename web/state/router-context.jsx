// Simple in-app router context with browser history support
(() => {
  const RouterContext = React.createContext(null);
  const VALID_ROUTES = ['login', 'chat', 'admin', 'docs', 'transition', 'reset-password'];

  function isValidRoute(route) {
    return VALID_ROUTES.includes(route);
  }

  // Map route names to URL paths and back
  const routeToPath = (route) => {
    if (!route || route === 'login') return '/';
    return `/${route}`;
  };

  const pathToRoute = (path) => {
    const clean = path.replace(/^\/+|\/+$/g, '');
    if (!clean || clean === 'login') return 'login';
    return clean;
  };

  function RouterProvider({ children }) {
    const userContext = window.CFC.UserContext.useUser();
    const { user, role, passwordRecoveryMode } = userContext || { user: null, role: 'user', passwordRecoveryMode: false };

    function getDefaultRouteForUser() {
      if (!user) return 'login';
      if (role === 'admin') return 'admin';
      return 'chat';
    }

    const getInitialRoute = () => {
      // Prefer the URL path first
      const urlRoute = pathToRoute(window.location.pathname);
      if (urlRoute && urlRoute !== 'login') {
        return urlRoute;
      }
      try {
        const savedRoute = window.localStorage.getItem('cfc-route');
        if (savedRoute && isValidRoute(savedRoute) && savedRoute !== 'transition') {
          return savedRoute;
        }
      } catch {
        // ignore
      }
      return 'login';
    };

    const [route, setRoute] = React.useState(getInitialRoute);
    const [nextRoute, setNextRoute] = React.useState(null);
    const [routeParams, setRouteParams] = React.useState({});
    const [visualState, setVisualState] = React.useState('idle');
    const hasInitialized = React.useRef(false);
    const prevRole = React.useRef(role);
    const skipPush = React.useRef(false); // avoid pushing when handling popstate

    React.useEffect(() => {
      // Password recovery takes priority — Supabase creates a temp session
      if (passwordRecoveryMode) {
        setRoute('reset-password');
        // Preserve the current hash so Supabase can still read recovery tokens from it
        const currentHash = window.location.hash || '';
        window.history.replaceState(
          { route: 'reset-password' },
          '',
          routeToPath('reset-password') + currentHash
        );
        return;
      }

      if (!user) {
        setRoute('login');
        try { window.localStorage.removeItem('cfc-route'); } catch { }
        hasInitialized.current = false;
        prevRole.current = 'user';
        window.history.replaceState({ route: 'login' }, '', routeToPath('login'));
      } else {
        // Detect when role changes from default 'user' to actual role (e.g. 'admin')
        // This happens because the role fetch is async and completes after user is set
        const roleJustResolved = prevRole.current !== role;
        prevRole.current = role;

        try {
          const savedRoute = window.localStorage.getItem('cfc-route');

          if (roleJustResolved && role === 'admin') {
            // Role just loaded as admin — override to admin default
            const defaultRoute = getDefaultRouteForUser();
            setRoute(defaultRoute);
            window.localStorage.setItem('cfc-route', defaultRoute);
            window.history.replaceState({ route: defaultRoute }, '', routeToPath(defaultRoute));
          } else if (savedRoute && isValidRoute(savedRoute) && savedRoute !== 'transition') {
            setRoute(savedRoute);
            window.history.replaceState({ route: savedRoute }, '', routeToPath(savedRoute));
          } else if (!hasInitialized.current) {
            const defaultRoute = getDefaultRouteForUser();
            setRoute(defaultRoute);
            window.localStorage.setItem('cfc-route', defaultRoute);
            window.history.replaceState({ route: defaultRoute }, '', routeToPath(defaultRoute));
          }
        } catch {
          // ignore
        }
        hasInitialized.current = true;
      }
    }, [user, role, passwordRecoveryMode]);

    // Listen for browser back/forward
    React.useEffect(() => {
      const handlePopState = (event) => {
        const newRoute = event.state?.route || pathToRoute(window.location.pathname);
        skipPush.current = true;
        try { window.localStorage.setItem('cfc-route', newRoute); } catch {}
        setRoute(newRoute);
        requestAnimationFrame(() => setVisualState('fade-in'));
        setTimeout(() => setVisualState('idle'), 280);
      };
      window.addEventListener('popstate', handlePopState);
      return () => window.removeEventListener('popstate', handlePopState);
    }, []);

    const performNavigation = React.useCallback((next, options = {}) => {
      if (next === 'transition') {
        setNextRoute(options.to || null);
      } else {
        setNextRoute(null);
        try { window.localStorage.setItem('cfc-route', next); } catch { }
      }
      setRouteParams(options.params || {});
      setRoute(next);

      // Push to browser history (unless triggered by popstate)
      if (!skipPush.current && next !== 'transition') {
        window.history.pushState({ route: next }, '', routeToPath(next));
      }
      skipPush.current = false;

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
      <RouterContext.Provider value={{ route, navigate, nextRoute, routeParams, visualState }}>
        {children}
      </RouterContext.Provider>
    );
  }

  function useRouter() {
    return React.useContext(RouterContext);
  }

  window.CFC = window.CFC || {};
  window.CFC.RouterContext = { RouterContext, RouterProvider, useRouter };
})();
