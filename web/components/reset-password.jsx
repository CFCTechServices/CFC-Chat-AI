// Reset password page — request reset link or set new password
(() => {
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { Layout } = window.CFC.Layout;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;
  const { useState } = React;

  function ResetPasswordPage() {
    const { supabase, passwordRecoveryMode, clearPasswordRecovery } = useUser();
    const { navigate } = useRouter();

    const [email, setEmail] = useState("");
    const [newPassword, setNewPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState("");
    const [success, setSuccess] = useState("");

    const isRecoveryMode = !!passwordRecoveryMode;

    const handleRequestReset = async (e) => {
      e.preventDefault();
      setLoading(true);
      setError("");
      setSuccess("");

      try {
        const res = await fetch("/api/auth/forgot-password", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email }),
        });
        const data = await res.json();
        if (data.success) {
          setSuccess(data.message);
        } else {
          setError(data.message || "Failed to send reset email.");
        }
      } catch (err) {
        setError("Failed to send reset email. Please try again.");
      } finally {
        setLoading(false);
      }
    };

    const handleSetNewPassword = async (e) => {
      e.preventDefault();
      setError("");

      if (newPassword.length < 6) {
        setError("Password must be at least 6 characters.");
        return;
      }
      if (newPassword !== confirmPassword) {
        setError("Passwords do not match.");
        return;
      }

      setLoading(true);
      try {
        const client = supabase || window.supabaseClient;
        const { error: updateError } = await client.auth.updateUser({
          password: newPassword,
        });

        if (updateError) throw updateError;

        setSuccess("Password updated successfully! Redirecting to login...");
        clearPasswordRecovery();
        setTimeout(() => navigate("login"), 2000);
      } catch (err) {
        setError(err.message || "Failed to update password.");
      } finally {
        setLoading(false);
      }
    };

    const msgStyle = (type) => ({
      padding: '10px 14px',
      borderRadius: '8px',
      fontSize: '0.9rem',
      backgroundColor: type === 'error'
        ? 'var(--color-error-bg, #fef2f2)'
        : 'var(--color-info-bg, #eff6ff)',
      color: type === 'error'
        ? 'var(--color-error, #dc2626)'
        : 'var(--color-info, #2563eb)',
      border: type === 'error'
        ? '1px solid var(--color-error-border, #fecaca)'
        : 'none',
    });

    return (
      <Layout>
        <div className="page login-page">
          <div className="login-hero">
            <h1>{isRecoveryMode ? "Set New Password" : "Reset Password"}</h1>
            <p>
              {isRecoveryMode
                ? "Enter your new password below."
                : "Enter your email and we'll send you a reset link."}
            </p>
          </div>

          <Card className="login-card">
            {isRecoveryMode ? (
              <form onSubmit={handleSetNewPassword} className="login-form">
                <h2>New Password</h2>
                <TextInput
                  label="New Password"
                  type="password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  placeholder="Enter new password"
                  autoComplete="new-password"
                />
                <TextInput
                  label="Confirm Password"
                  type="password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  placeholder="Confirm new password"
                  autoComplete="new-password"
                />
                {error && <div style={msgStyle('error')}>{error}</div>}
                {success && <div style={msgStyle('success')}>{success}</div>}
                <PrimaryButton type="submit" disabled={loading || !newPassword || !confirmPassword}>
                  {loading ? "Updating..." : "Set New Password"}
                </PrimaryButton>
              </form>
            ) : (
              <form onSubmit={handleRequestReset} className="login-form">
                <h2>Forgot Password</h2>
                <TextInput
                  label="Email Address"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="Enter your email"
                  autoComplete="email"
                />
                {error && <div style={msgStyle('error')}>{error}</div>}
                {success && <div style={msgStyle('success')}>{success}</div>}
                <PrimaryButton type="submit" disabled={loading || !email}>
                  {loading ? "Sending..." : "Send Reset Link"}
                </PrimaryButton>
                <div
                  style={{ marginTop: '15px', textAlign: 'center', cursor: 'pointer', textDecoration: 'underline', fontSize: '0.9rem' }}
                  onClick={() => navigate('login')}
                >
                  Back to Sign In
                </div>
              </form>
            )}
          </Card>
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.ResetPasswordPage = ResetPasswordPage;
})();
