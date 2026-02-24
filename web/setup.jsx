// Setup global namespaces and contexts
(() => {
    window.CFC = window.CFC || {};

    const { createContext, useContext, useState, useEffect, useMemo } = React;

    // --- Theme Context ---
    const ThemeContext = createContext(null);

    function ThemeProvider({ children }) {
        const [isDark, setIsDark] = useState(false);

        useEffect(() => {
            // Check local storage or system preference
            const saved = localStorage.getItem('cfc-theme');
            if (saved) {
                setIsDark(saved === 'dark');
            } else if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
                setIsDark(true);
            }
        }, []);

        useEffect(() => {
            const root = document.documentElement;
            if (isDark) {
                root.classList.add('dark-mode');
                root.setAttribute('data-theme', 'dark');
            } else {
                root.classList.remove('dark-mode');
                root.setAttribute('data-theme', 'light');
            }
            localStorage.setItem('cfc-theme', isDark ? 'dark' : 'light');
        }, [isDark]);

        const toggleTheme = () => setIsDark(!isDark);

        return (
            <ThemeContext.Provider value={{ isDark, toggleTheme }}>
                {children}
            </ThemeContext.Provider>
        );
    }

    const useTheme = () => useContext(ThemeContext);
    window.CFC.ThemeContext = { ThemeContext, ThemeProvider, useTheme };


    // --- User Context (Supabase) ---
    const UserContext = createContext(null);

    function UserProvider({ children }) {
        const [user, setUser] = useState(null);
        const [session, setSession] = useState(null);
        const [role, setRole] = useState('user');
        const [loading, setLoading] = useState(true);

        useEffect(() => {
            // Fetch Config & Init Supabase
            fetch("/api/auth/config")
                .then(res => res.json())
                .then(config => {
                    if (window.supabase) {
                        window.supabaseClient = window.supabase.createClient(config.supabaseUrl, config.supabaseKey);

                        // Check Session
                        window.supabaseClient.auth.getSession().then(({ data: { session } }) => {
                            setSession(session);
                            if (session) {
                                setUser({ ...session.user, name: session.user.email }); // Normalize for existing components
                                fetchRole(session.user.id);
                            } else {
                                setLoading(false);
                            }
                        });

                        const { data: { subscription } } = window.supabaseClient.auth.onAuthStateChange((_event, session) => {
                            setSession(session);
                            if (session) {
                                setUser({ ...session.user, name: session.user.email });
                                fetchRole(session.user.id);
                            } else {
                                setUser(null);
                                setRole('user');
                                setLoading(false);
                            }
                        });

                        return () => subscription.unsubscribe();
                    }
                });
        }, []);

        const fetchRole = async (userId) => {
            try {
                const { data } = await window.supabaseClient
                    .from('profiles')
                    .select('role')
                    .eq('id', userId)
                    .single();

                if (data && data.role) {
                    setRole(data.role);
                }
            } catch (e) {
                console.error("Error fetching role", e);
            } finally {
                setLoading(false);
            }
        };

        // For compatibility with existing login.jsx which calls setUser
        // We probably won't use this manual setUser much with Supabase, but let's keep it safe.
        const manualSetUser = (u) => setUser(u);

        const value = useMemo(() => ({
            user,
            session,
            role,
            loading,
            setUser: manualSetUser, // Backward compat
            supabase: window.supabaseClient
        }), [user, session, role, loading]);

        return (
            <UserContext.Provider value={value}>
                {children}
            </UserContext.Provider>
        );
    }

    const useUser = () => useContext(UserContext);
    window.CFC.UserContext = { UserContext, UserProvider, useUser };

    // --- Router Context ---
    const RouterContext = createContext(null);

    function RouterProvider({ children }) {
        const [route, setRoute] = useState('login'); // login | transition | chat | admin
        const [visualState, setVisualState] = useState(''); // for transitions
        const { user, loading } = useUser();

        // Redirect logic
        useEffect(() => {
            if (!loading) {
                if (user && route === 'login') {
                    setRoute('chat');
                } else if (!user && route !== 'login' && route !== 'transition') {
                    setRoute('login');
                }
            }
        }, [user, loading]);

        const navigate = (newRoute, options = {}) => {
            if (options.withFade) {
                setVisualState('fade-out');
                setTimeout(() => {
                    setRoute(newRoute);
                    setVisualState('fade-in');
                    setTimeout(() => setVisualState(''), 300);
                }, 300);
            } else {
                setRoute(newRoute);
            }
        };

        return (
            <RouterContext.Provider value={{ route, navigate, visualState }}>
                {children}
            </RouterContext.Provider>
        );
    }

    const useRouter = () => useContext(RouterContext);
    window.CFC.RouterContext = { RouterContext, RouterProvider, useRouter };

})();
