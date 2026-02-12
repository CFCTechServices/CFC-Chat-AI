// Simple in-app router context (no external dependency)
(() => {
  const RouterContext = React.createContext(null);

  function RouterProvider({ children }) {
    const userContext = window.CFC.UserContext.useUser();
    const { user, role } = userContext || { user: null, role: 'user' };

    function getDefaultRouteForUser() {
      if (!user) return 'login';
      // Use role from backend instead of email
      if (role === 'admin') return 'admin';
      return 'chat';
    }

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

    const [route, setRoute] = React.useState(getInitialRoute);
    const [nextRoute, setNextRoute] = React.useState(null);
    const [visualState, setVisualState] = React.useState('idle');
    const hasInitialized = React.useRef(false);

    React.useEffect(() => {
      if (!user) {
        setRoute('login');
        try { window.localStorage.removeItem('cfc-route'); } catch { }
        hasInitialized.current = false;
      } else {
        try {
          const savedRoute = window.localStorage.getItem('cfc-route');
          if (savedRoute && savedRoute !== 'transition') {
            setRoute(savedRoute);
          } else if (!hasInitialized.current) {
            const defaultRoute = getDefaultRouteForUser();
            setRoute(defaultRoute);
            window.localStorage.setItem('cfc-route', defaultRoute);
          }
        } catch {
          // ignore
        }
        hasInitialized.current = true;
      }
    }, [user, role]);

    const performNavigation = React.useCallback((next, options = {}) => {
      if (next === 'transition') {
        setNextRoute(options.to || null);
      } else {
        setNextRoute(null);
        try { window.localStorage.setItem('cfc-route', next); } catch { }
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

  window.CFC = window.CFC || {};
  window.CFC.RouterContext = { RouterContext, RouterProvider, useRouter };
})();
