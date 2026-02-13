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

    // Apply display preferences on initial load
    React.useEffect(() => {
      try {
        const textSize = window.localStorage.getItem('cfc-text-size') || 'medium';
        const highContrast = window.localStorage.getItem('cfc-high-contrast') === 'on';
        const reducedMotion = window.localStorage.getItem('cfc-reduced-motion') === 'on';

        const html = document.documentElement;
        html.classList.toggle('text-size-small', textSize === 'small');
        html.classList.toggle('text-size-medium', textSize === 'medium');
        html.classList.toggle('text-size-large', textSize === 'large');
        html.classList.toggle('high-contrast', highContrast);
        html.classList.toggle('reduced-motion', reducedMotion);
      } catch {
        // ignore storage errors
      }
    }, []);

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
