// Transition splash page while navigating
(() => {
  const { Layout } = window.CFC.Layout;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;

  function TransitionPage() {
    const { user } = useUser();
    const { nextRoute, navigate } = useRouter();

    React.useEffect(() => {
      const id = setTimeout(() => {
        navigate(nextRoute || 'login', { withFade: true });
      }, 1800);
      return () => clearTimeout(id);
    }, [nextRoute, navigate]);

    const firstName = (user?.name || 'there').split(' ')[0];

    return (
      <Layout>
        <div className="page transition-screen">
          <div className="transition-inner standalone">
            <h1 className="transition-title">Welcome, {firstName}!</h1>
            <p className="muted">
              We&apos;re getting everything ready for you.
              <br />
              This will only take a moment.
            </p>
            <div className="transition-pill">Preparing your workspace</div>
            <div className="transition-loader">
              <div className="dot" />
              <div className="dot" />
              <div className="dot" />
            </div>
          </div>
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.TransitionPage = TransitionPage;
})();
