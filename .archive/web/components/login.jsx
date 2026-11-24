// Login page with email-whitelist signup flow
(() => {
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { Layout } = window.CFC.Layout;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;
  const { useState, useEffect } = React;

  function LoginPage() {
    // Steps: 1 = enter email, 2 = enter password (sign-up or sign-in)
    const [step, setStep] = useState(2); // default to sign-in
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState("");
    const [isSignUp, setIsSignUp] = useState(false);

    const { supabase, loading: authLoading } = useUser();
    const { navigate } = useRouter();
    const clientReady = !!supabase || !!window.supabaseClient;

    // Check if email is on the invitation whitelist
    const handleEmailCheck = async (e) => {
      e.preventDefault();
      setLoading(true);
      setError("");
      try {
        const res = await fetch("/api/auth/check-email", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email }),
        });
        const data = await res.json();
        if (data.eligible) {
          setStep(2);
          setIsSignUp(true);
        } else {
          setError(data.message || "This email is not eligible for registration.");
        }
      } catch (err) {
        setError("Failed to verify email. Please try again.");
      } finally {
        setLoading(false);
      }
    };

    const handleAuth = async (e) => {
      e.preventDefault();
      setLoading(true);
      setError("");

      try {
        // Use context value or fall back to window global
        const client = supabase || window.supabaseClient;
        if (!client) throw new Error("Authentication service is loading. Please try again in a moment.");

        let result;
        if (isSignUp) {
          if (password !== confirmPassword) {
            throw new Error("Passwords do not match.");
          }
          result = await client.auth.signUp({ email, password });
        } else {
          result = await client.auth.signInWithPassword({ email, password });
        }

        if (result.error) throw result.error;

        if (isSignUp && !result.data.session) {
          setError("Sign up successful! Please check your email to confirm.");
          setIsSignUp(false);
          setStep(2);
        }
        // If session exists, UserProvider will update automatically via onAuthStateChange
        // and will call /api/auth/confirm-registration to mark the invite as registered
      } catch (err) {
        console.error(err);
        let errorMessage = err.message;
        if (errorMessage && errorMessage.toLowerCase().includes('banned')) {
          errorMessage = 'Account is inactive';
        }
        setError(errorMessage);
      } finally {
        setLoading(false);
      }
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
            {step === 1 ? (
              <form onSubmit={handleEmailCheck} className="login-form">
                <h2>Sign Up</h2>
                <p>Enter the email address your administrator invited.</p>
                <TextInput
                  label="Email Address"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="Enter your invited email"
                  autoComplete="email"
                />
                {error && (
                  <div style={{
                    padding: '10px 14px',
                    borderRadius: '8px',
                    backgroundColor: 'var(--color-error-bg, #fef2f2)',
                    color: 'var(--color-error, #dc2626)',
                    fontSize: '0.9rem',
                    border: '1px solid var(--color-error-border, #fecaca)'
                  }}>
                    {error}
                  </div>
                )}
                <PrimaryButton type="submit" disabled={loading || !email}>
                  {loading ? "Checking..." : "Next"}
                </PrimaryButton>
                <div
                  style={{ marginTop: '15px', textAlign: 'center', cursor: 'pointer', textDecoration: 'underline', fontSize: '0.9rem' }}
                  onClick={() => { setStep(2); setIsSignUp(false); setError(''); }}
                >
                  Already have an account? Sign In
                </div>
              </form>
            ) : (
              <form onSubmit={handleAuth} className="login-form">
                <h2>{isSignUp ? "Create Account" : "Sign In"}</h2>
                <p>Access is restricted to authorized users only.</p>
                {isSignUp && (
                  <div style={{
                    padding: '10px 14px',
                    borderRadius: '8px',
                    backgroundColor: 'var(--color-info-bg, #eff6ff)',
                    color: 'var(--color-info, #2563eb)',
                    fontSize: '0.9rem',
                    marginBottom: '12px',
                  }}>
                    Creating account for: <strong>{email}</strong>
                  </div>
                )}
                {!isSignUp && (
                  <TextInput
                    label="Email Address"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="Enter your email"
                    autoComplete="email"
                  />
                )}
                <TextInput
                  label="Password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter your password"
                  autoComplete={isSignUp ? "new-password" : "current-password"}
                />
                {isSignUp && (
                  <TextInput
                    label="Confirm Password"
                    type="password"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    placeholder="Confirm your password"
                    autoComplete="new-password"
                  />
                )}
                {!isSignUp && (
                  <div
                    style={{ textAlign: 'right', cursor: 'pointer', textDecoration: 'underline', fontSize: '0.85rem', color: 'var(--color-text-muted, #666)', marginTop: '-4px' }}
                    onClick={() => navigate('reset-password')}
                  >
                    Forgot Password?
                  </div>
                )}
                {error && (
                  <div style={{
                    padding: '10px 14px',
                    borderRadius: '8px',
                    backgroundColor: error.startsWith('Sign up successful') ? 'var(--color-success-bg, #f0fdf4)' : 'var(--color-error-bg, #fef2f2)',
                    color: error.startsWith('Sign up successful') ? 'var(--color-success, #16a34a)' : 'var(--color-error, #dc2626)',
                    fontSize: '0.9rem',
                    border: `1px solid ${error.startsWith('Sign up successful') ? 'var(--color-success-border, #bbf7d0)' : 'var(--color-error-border, #fecaca)'}`
                  }}>
                    {error}
                  </div>
                )}
                <PrimaryButton type="submit" disabled={loading || !email || !password || (isSignUp && !confirmPassword) || !clientReady}>
                  {!clientReady ? "Connecting..." : loading ? "Processing..." : (isSignUp ? "Sign Up" : "Sign In")}
                </PrimaryButton>
                <div
                  style={{ marginTop: '15px', textAlign: 'center', cursor: 'pointer', textDecoration: 'underline', fontSize: '0.9rem' }}
                  onClick={() => {
                    setError('');
                    setPassword('');
                    setConfirmPassword('');
                    if (isSignUp) {
                      setIsSignUp(false);
                    } else {
                      setStep(1);
                      setIsSignUp(true);
                      setEmail('');
                    }
                  }}
                >
                  {isSignUp ? "Already have an account? Sign In" : "Need an account? Sign Up"}
                </div>
                <p className="footer-text">
                  Don't have access? Contact your administrator to request access.
                </p>
              </form>
            )}
          </Card>
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.LoginPage = LoginPage;
})();
