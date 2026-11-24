// Main App Entry Point
(() => {
    const { ThemeProvider } = window.CFC.ThemeContext;
    const { UserProvider } = window.CFC.UserContext;
    const { RouterProvider, useRouter } = window.CFC.RouterContext;

    // Pages
    const { LoginPage } = window.CFC.Pages;
    const { ChatPage } = window.CFC.Pages;
    const { AdminPage } = window.CFC.Pages;
    // Transition page might be useful later, for now we map directly

    function MainRouter() {
        const { route } = useRouter();

        switch (route) {
            case 'login':
                return <LoginPage />;
            case 'chat':
                return <ChatPage />;
            case 'admin':
                return <AdminPage />;
            case 'transition':
                // Simple placeholder for transition state if used by existing login.jsx
                return <div className="page-fader">Loading...</div>;
            default:
                return <LoginPage />;
        }
    }

    function App() {
        return (
            <ThemeProvider>
                <UserProvider>
                    <RouterProvider>
                        <MainRouter />
                    </RouterProvider>
                </UserProvider>
            </ThemeProvider>
        );
    }

    const root = ReactDOM.createRoot(document.getElementById("root"));
    root.render(<App />);
})();
