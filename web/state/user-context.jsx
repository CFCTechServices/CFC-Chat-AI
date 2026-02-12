// User context with Supabase authentication
(() => {
  const { createContext, useContext, useState, useEffect, useMemo, useRef } = React;
  const UserContext = createContext(null);

  function useUser() {
    return useContext(UserContext);
  }

  function UserProvider({ children }) {
    const [user, setUser] = useState(null);
    const [session, setSession] = useState(null);
    const [role, setRole] = useState('user');
    const [loading, setLoading] = useState(true);
    const [profile, setProfile] = useState(null);
    const [sbClient, setSbClient] = useState(null);
    const initStarted = useRef(false);

    useEffect(() => {
      // Prevent double-init in strict mode
      if (initStarted.current) return;
      initStarted.current = true;

      const fetchRoleForUser = async (client, userId) => {
        try {
          const { data } = await client
            .from('profiles')
            .select('role, full_name, avatar_url, status')
            .eq('id', userId)
            .single();

          if (data) {
            if (data.role) setRole(data.role);
            setProfile(data);
          }
        } catch (e) {
          console.error('Error fetching role:', e);
        } finally {
          setLoading(false);
        }
      };

      const handleSession = (client, sess) => {
        setSession(sess);
        if (sess) {
          setUser({ ...sess.user, name: sess.user.email });
          fetchRoleForUser(client, sess.user.id);
        } else {
          setUser(null);
          setRole('user');
          setProfile(null);
          setLoading(false);
        }
      };

      fetch('/api/auth/config')
        .then(res => res.json())
        .then(config => {
          if (!window.supabase) {
            console.error('Supabase JS library not loaded');
            setLoading(false);
            return;
          }

          const client = window.supabase.createClient(
            config.supabaseUrl,
            config.supabaseKey
          );
          window.supabaseClient = client;
          setSbClient(client);

          // Check existing session
          client.auth.getSession().then(({ data: { session } }) => {
            handleSession(client, session);
          });

          // Listen for auth state changes (login, logout, token refresh)
          client.auth.onAuthStateChange((_event, session) => {
            handleSession(client, session);
          });
        })
        .catch(err => {
          console.error('Failed to fetch auth config:', err);
          setLoading(false);
        });
    }, []);

    const value = useMemo(() => ({
      user,
      session,
      role,
      loading,
      profile,
      setUser,
      setProfile,
      supabase: sbClient
    }), [user, session, role, loading, profile, sbClient]);

    return (
      <UserContext.Provider value={value}>
        {children}
      </UserContext.Provider>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.UserContext = { UserContext, useUser, UserProvider };
})();
