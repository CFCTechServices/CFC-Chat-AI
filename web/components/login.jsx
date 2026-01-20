// Login page
(() => {
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { Layout } = window.CFC.Layout;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;

  function LoginPage() {
    const [name, setName] = React.useState('');
    const [email, setEmail] = React.useState('');
    const [emailError, setEmailError] = React.useState('');
    const { setUser } = useUser();
    const { navigate } = useRouter();

    const validateEmail = (value) => {
      const trimmed = value.trim();
      const re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      return re.test(trimmed);
    };

    const handleSubmit = (e) => {
      e.preventDefault();
      const trimmedEmail = email.trim();
      if (!validateEmail(trimmedEmail)) {
        setEmailError('Please enter a valid email address.');
        return;
      }
      setEmailError('');

      const rawName = name.trim();
      let displayName = 'Guest';
      if (rawName) {
        const parts = rawName.split(/\s+/);
        const first = parts[0];
        const capFirst = first.charAt(0).toUpperCase() + first.slice(1).toLowerCase();
        const rest = parts.slice(1).join(' ');
        displayName = rest ? `${capFirst} ${rest}` : capFirst;
      }

      const nextUser = { name: displayName, email: trimmedEmail };
      setUser(nextUser);

      const lower = trimmedEmail.toLowerCase();
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
              <TextInput
                label="Name"
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Adam Smith"
                autoComplete="name"
              />
              <TextInput
                label="Email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="you@cfctech.com"
                autoComplete="email"
                error={emailError}
              />
              <PrimaryButton type="submit">Continue</PrimaryButton>
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
