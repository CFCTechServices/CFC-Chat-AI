// User context with Supabase authentication
(() => {
  const { createContext, useContext, useState, useEffect, useMemo, useRef } = React;
  const UserContext = createContext(null);

  // Detect recovery tokens from the URL BEFORE React renders anything.
  // This must happen synchronously at module load time because the router's
  // useEffect fires before ours and will replaceState('/'), stripping the hash.
  const _initialHash = window.location.hash || '';
  const _initialSearch = window.location.search || '';
  const _isRecoveryUrl = _initialHash.includes('type=recovery')
    || _initialSearch.includes('type=recovery');

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
    const [passwordRecoveryMode, setPasswordRecoveryMode] = useState(_isRecoveryUrl);
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
            // If we fetched a full_name for the profile, prefer it for the
            // user display name so header greetings show a real name
            // instead of the email address.
            if (data.full_name) {
              // Merge the profile full_name into the user state. Use a
              // fallback empty object so this works even if the initial
              // session-driven setUser hasn't flushed yet (avoids a race
              // where `prev` is null and the name would not be applied).
              setUser(prev => ({ ...(prev || {}), name: data.full_name }));
            }
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

          // Register auth listener BEFORE getSession() so PASSWORD_RECOVERY
          // events from hash processing are not missed.
          const { data: { subscription } } = client.auth.onAuthStateChange((event, session) => {
            if (event === 'PASSWORD_RECOVERY') {
              setPasswordRecoveryMode(true);
            }
            handleSession(client, session);
          });

          // Check existing session (will trigger onAuthStateChange if hash tokens exist)
          client.auth.getSession().then(({ data: { session } }) => {
            // Only handle here if onAuthStateChange hasn't already provided a session
            if (session) {
              handleSession(client, session);
            }
          });
        })
        .catch(err => {
          console.error('Failed to fetch auth config:', err);
          setLoading(false);
        });
    }, []);

    const clearPasswordRecovery = () => setPasswordRecoveryMode(false);

    const value = useMemo(() => ({
      user,
      session,
      role,
      loading,
      profile,
      setUser,
      setProfile,
      supabase: sbClient,
      passwordRecoveryMode,
      clearPasswordRecovery,
    }), [user, session, role, loading, profile, sbClient, passwordRecoveryMode]);

    return (
      <UserContext.Provider value={value}>
        {children}
      </UserContext.Provider>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.UserContext = { UserContext, useUser, UserProvider };
})();
