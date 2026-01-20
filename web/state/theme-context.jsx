// Theme (dark mode) context and provider
(() => {
  const ThemeContext = React.createContext(null);

  function useTheme() {
    return React.useContext(ThemeContext);
  }

  function ThemeProvider({ children }) {
    const [isDark, setIsDark] = React.useState(() => {
      try {
        const saved = window.localStorage.getItem('cfc-theme');
        const shouldBeDark = saved === 'dark';
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
        // ignore storage errors
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

  window.CFC = window.CFC || {};
  window.CFC.ThemeContext = { ThemeContext, useTheme, ThemeProvider };
})();
