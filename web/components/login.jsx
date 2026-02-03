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

    // Auto-detect invite code from URL
    useEffect(() => {
      const params = new URLSearchParams(window.location.search);
      const code = params.get("code") || params.get("invite");
      if (code) {
        setInviteCode(code);
        setStep(1);
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

      // Use Supabase client from global or context
      // Context might not be fully ready if we are racing, but window.supabaseClient is set in setup.jsx
      // Better to use window.supabaseClient as initialized in setup.jsx
      // or cleaner: const { supabase } = useUser();

      try {
        const supabase = window.supabaseClient;
        if (!supabase) throw new Error("Supabase client not initialized");

        let result;
        if (isSignUp) {
          result = await supabase.auth.signUp({ email, password });
        } else {
          result = await supabase.auth.signInWithPassword({ email, password });
        }

        if (result.error) throw result.error;

        if (isSignUp && !result.data.session) {
          setError("Sign up successful! Please check your email to confirm.");
          setIsSignUp(false); // Switch to login view
        }
        // If session exists, UserProvider will reject automatically via onAuthStateChange
      } catch (err) {
        console.error(err);
        setError(err.message);
      } finally {
        setLoading(false);
      }
    };

    return (
      <Layout>
        <div className="page login-page">
          <div className="login-hero">
            <h1>Welcome to CFC Chat-Talk</h1>
            <p>AI-powered assistance for your animal feed software queries.</p>
            <div className="login-badges">
              <span className="badge badge-sand">AI Powered</span>
              <span className="badge badge-green">Docs Search</span>
              <span className="badge badge-blue">Secure</span>
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
                  onClick={() => { setStep(2); setIsSignUp(false); }}
                >
                  Already have an account? Sign In
                </div>
              </form>
            ) : (
              <form onSubmit={handleAuth} className="login-form">
                <h2>{isSignUp ? "Create Account" : "Sign In"}</h2>
                <TextInput
                  label="Email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@cfctech.com"
                  autoComplete="email"
                />
                <TextInput
                  label="Password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••"
                  autoComplete="current-password"
                />
                {error && <div className="field-error-text">{error}</div>}

                <PrimaryButton type="submit" disabled={loading || !email || !password}>
                  {loading ? "Processing..." : (isSignUp ? "Sign Up" : "Sign In")}
                </PrimaryButton>

                <div
                  style={{ marginTop: '15px', textAlign: 'center', cursor: 'pointer', textDecoration: 'underline', fontSize: '0.9rem' }}
                  onClick={() => {
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
                {isSignUp && (
                  <div
                    style={{ marginTop: '10px', textAlign: 'center', cursor: 'pointer', fontSize: '0.8rem' }}
                    onClick={() => { setStep(2); setIsSignUp(false); }}
                  >
                    ← Back to Login
                  </div>
                )}
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
