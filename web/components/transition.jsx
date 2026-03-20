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
          <div className="transition-inner standalone" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', textAlign: 'center' }}>
            <img
              src="/ui/logo-cfc.png"
              alt="CFC Tech"
              className="app-logo"
              style={{ width: 240, display: 'block', margin: '0 auto' }}
            />
            <div className="transition-pill" style={{ marginTop: 8, display: 'inline-flex', justifyContent: 'center', alignItems: 'center' }}>Preparing your workspace</div>
            <div className="transition-loader" style={{ marginTop: 12 }}>
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
