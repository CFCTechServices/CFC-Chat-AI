// User context and provider
(() => {
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
        // ignore storage errors
      }
    }, []);

    return (
      <UserContext.Provider value={{ user, setUser: updateUser }}>
        {children}
      </UserContext.Provider>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.UserContext = { UserContext, useUser, UserProvider };
})();
