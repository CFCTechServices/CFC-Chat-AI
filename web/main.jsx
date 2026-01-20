// Entry point: compose providers and routes
(() => {
  const { ThemeProvider } = window.CFC.ThemeContext;
  const { UserProvider } = window.CFC.UserContext;
  const { RouterProvider, useRouter } = window.CFC.RouterContext;
  const { LoginPage, DocsPage, AdminPage, ChatPage, TransitionPage } = window.CFC.Pages;

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

  ReactDOM.createRoot(document.getElementById('root')).render(<App />);
})();
