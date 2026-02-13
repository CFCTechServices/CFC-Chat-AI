// Login page with Invite and Supabase Auth
(() => {
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { Layout } = window.CFC.Layout;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;
  const { useState, useEffect } = React;

  function LoginPage() {
    const [step, setStep] = useState(2); // 1: Invite, 2: Login
    const [inviteCode, setInviteCode] = useState("");
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState("");
    const [isSignUp, setIsSignUp] = useState(false);
    const [inviteEmail, setInviteEmail] = useState("");

    const { supabase, loading: authLoading } = useUser();
    const { navigate } = useRouter();
    const clientReady = !!supabase || !!window.supabaseClient;

    // Auto-detect invite code and email from URL
    useEffect(() => {
      const params = new URLSearchParams(window.location.search);
      const code = params.get("code") || params.get("invite");
      const emailParam = params.get("email");

      if (code) {
        setInviteCode(code);
        setStep(1);
        if (emailParam) {
          setEmail(emailParam);
          setInviteEmail(emailParam);
        }
      }
    }, []);

    const handleInviteSubmit = async (e) => {
      e.preventDefault();
      setLoading(true);
      setError("");
      try {
        const res = await fetch("/api/auth/validate-invite", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ invite_code: inviteCode }),
        });
        const data = await res.json();
        if (data.valid) {
          if (data.email) {
            setEmail(data.email);
            setInviteEmail(data.email);
          }
          setStep(2);
          setIsSignUp(true);
        } else {
          setError(data.message || "Invalid invite code");
        }
      } catch (err) {
        setError("Failed to validate invite code");
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
          if (inviteEmail && email !== inviteEmail) {
            throw new Error(`Email must match the invited email: ${inviteEmail}`);
          }
          result = await client.auth.signUp({
            email,
            password,
            options: {
              data: {
                invite_code: inviteCode
              }
            }
          });
        } else {
          result = await client.auth.signInWithPassword({ email, password });
        }

        if (result.error) throw result.error;

        if (isSignUp && !result.data.session) {
          setError("Sign up successful! Please check your email to confirm.");
          setIsSignUp(false);
        }
        // If session exists, UserProvider will update automatically via onAuthStateChange
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
              <form onSubmit={handleInviteSubmit} className="login-form">
                <h2>Enter Invite Code</h2>
                <TextInput
                  label="Invite Code"
                  type="text"
                  value={inviteCode}
                  onChange={(e) => setInviteCode(e.target.value)}
                  placeholder="e.g. CFC-2024-X8Y"
                  error={error}
                />
                <PrimaryButton type="submit" disabled={loading || !inviteCode}>
                  {loading ? "Validating..." : "Continue"}
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
                {inviteEmail && isSignUp && (
                  <div style={{
                    padding: '10px 14px',
                    borderRadius: '8px',
                    backgroundColor: 'var(--color-info-bg, #eff6ff)',
                    color: 'var(--color-info, #2563eb)',
                    fontSize: '0.9rem',
                    marginBottom: '12px',
                  }}>
                    This invitation is for: <strong>{inviteEmail}</strong>
                  </div>
                )}
                <TextInput
                  label="Email Address"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="Enter your email"
                  autoComplete="email"
                  disabled={!!inviteEmail && isSignUp}
                />
                <TextInput
                  label="Password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter your password"
                  autoComplete="current-password"
                />
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
                    backgroundColor: 'var(--color-error-bg, #fef2f2)',
                    color: 'var(--color-error, #dc2626)',
                    fontSize: '0.9rem',
                    border: '1px solid var(--color-error-border, #fecaca)'
                  }}>
                    {error}
                  </div>
                )}
                <PrimaryButton type="submit" disabled={loading || !email || !password || !clientReady}>
                  {!clientReady ? "Connecting..." : loading ? "Processing..." : (isSignUp ? "Sign Up" : "Sign In")}
                </PrimaryButton>
                <div
                  style={{ marginTop: '15px', textAlign: 'center', cursor: 'pointer', textDecoration: 'underline', fontSize: '0.9rem' }}
                  onClick={() => {
                    setError('');
                    if (isSignUp) {
                      setIsSignUp(false);
                    } else {
                      setStep(1);
                      setIsSignUp(true);
                    }
                  }}
                >
                  {isSignUp ? "Already have an account? Sign In" : "Need an account? Redeem Invite"}
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
