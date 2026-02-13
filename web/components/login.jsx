// Login page

(() => {
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { Layout } = window.CFC.Layout;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;

  function LoginPage() {
    const [email, setEmail] = React.useState('');
    const [password, setPassword] = React.useState('');
    const [passwordError, setPasswordError] = React.useState('');
    const [emailError, setEmailError] = React.useState('');

    const { setUser } = useUser();
    const { navigate } = useRouter();

    const validateEmail = (value) => {
      const trimmed = value.trim();
      const re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      return re.test(trimmed);
    };
    const extractDisplayName = (email) => {

      const rawName = email.trim();
      let displayName = 'Guest';
      if (rawName && rawName.includes('@')) {
        const parts = rawName.split('@')[0].split(/[\._\-]+/).filter(Boolean).filter(p => isNaN(p));
        if (parts.length > 0) {
          displayName = parts.map(p => p.charAt(0).toUpperCase() + p.slice(1).toLowerCase()).join(' ');
        }
      }
      return displayName;
    };
    const handleSubmit = async (e) => {
      e.preventDefault();
      if (!validateEmail(email.trim())) {
        setEmailError('Please enter a valid email address.');
        return;
      } else {
        setEmailError('');
      }
      setUser({ email, displayName: extractDisplayName(email) , password});
      const lower = email.trim().toLowerCase();
      let target = 'chat';
      if (lower === 'admin@cfctech.com') target = 'admin';

      navigate('transition', { to: target, withFade: true });
    };
    return (
      <Layout>  
        <div className="page login-page">
          <div className="login-hero">
            <h1>Welcome to CFC AI</h1>
            <p>
              A focused assistant for Concept5 and CFC knowledge.
              <br />
              Ask clear questions and get concise, guided answers in seconds.
            </p>
            <div className="login-badges">
              <span className="badge badge-sand">Built for your workflows</span>
              <span className="badge badge-green">Instant answers</span>
              <span className="badge badge-blue">Guided help</span>
            </div>
          </div>


          <Card className="login-card">
            <form onSubmit={handleSubmit} className="login-form">
              <h2>Sign in</h2>
              <p> Access is restricted to authorized users only. 
              </p>
              <TextInput
                label="Email Address"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="Enter your email"
                autoComplete="email"
                error={emailError}
              />
              <TextInput
                label="Password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter your password"
                autoComplete="current-password"
              />
              <PrimaryButton type="submit">
                {'Continue'}
              </PrimaryButton>
              <p className="footer-text">
                  Don't have access? Contact your administrator to request access.
                </p>
            </form>
              
          </Card>

        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.LoginPage = LoginPage;
})();
