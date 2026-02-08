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
    const [inviteEmail, setInviteEmail] = useState(""); // Email from invite code

    // Auto-detect invite code and email from URL
    useEffect(() => {
      const params = new URLSearchParams(window.location.search);
      const code = params.get("code") || params.get("invite");
      const emailParam = params.get("email");

      if (code) {
        setInviteCode(code);
        setStep(1);

        // If email is in URL, pre-fill it
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
          // Store the email from the invite
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
        const supabase = window.supabaseClient;
        if (!supabase) throw new Error("Supabase client not initialized");

        let result;
        if (isSignUp) {
          // Validate email matches invite email
          if (inviteEmail && email !== inviteEmail) {
            throw new Error(`Email must match the invited email: ${inviteEmail}`);
          }

          // Pass invite_code in user metadata for the database trigger
          result = await supabase.auth.signUp({
            email,
            password,
            options: {
              data: {
                invite_code: inviteCode
              }
            }
          });
        } else {
          result = await supabase.auth.signInWithPassword({ email, password });
        }

        if (result.error) throw result.error;

        if (isSignUp && !result.data.session) {
          setError("Sign up successful! Please check your email to confirm.");
          setIsSignUp(false); // Switch to login view
        }
        // If session exists, UserProvider will update automatically via onAuthStateChange
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
                {inviteEmail && isSignUp && (
                  <div className="invite-info-box">
                    ℹ️ This invitation is for: <strong>{inviteEmail}</strong>
                  </div>
                )}
                <TextInput
                  label="Email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@cfctech.com"
                  autoComplete="email"
                  disabled={!!inviteEmail}
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
