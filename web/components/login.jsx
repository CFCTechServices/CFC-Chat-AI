// Login page

(() => {
  const { Card, PrimaryButton, TextInput } = window.CFC.Primitives;
  const { Layout } = window.CFC.Layout;
  const { useUser } = window.CFC.UserContext;
  const { useRouter } = window.CFC.RouterContext;

  function LoginPage() {
    const [step, setStep] = React.useState('credentials'); // 'credentials' or 'otp'
    const [email, setEmail] = React.useState('');
    const [password, setPassword] = React.useState('');
    const [otp, setOtp] = React.useState('');
    const [otpError, setOtpError] = React.useState('');
    const [isVerifying, setIsVerifying] = React.useState(false);
    const [emailError, setEmailError] = React.useState('');
    const [error, setError] = React.useState(null);

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
    const handleRequestOtp = (e) => {
      e.preventDefault();
      setError(null);
      if (!validateEmail(email) || password.trim() === '') {
        setError('Please enter a valid email or password.');
        return;
      }
      setEmailError('');
      setIsVerifying(true);
      // This would call: POST /api/auth/request-otp 
      setTimeout(() => {
        setIsVerifying(false);
        const hasAccess = true; // this comes from backend response for verification
        if (!hasAccess) {
          setError('You do not have access to CFC AI. Please contact support.');
          return;
        }
        else {
          setStep('otp');
        }
        
      }, 1000);

    };
    const handleSendOtp = (method) => {
      // This would call: POST /api/auth/send-otp
    // with payload: { email, method: "email" }
      setError("");
      console.log(`Simulating sending OTP via ${method}...`);
    };
     const handleVerifyOtp = (e) => {
      e.preventDefault();
      setError("");
      if (!otp.trim()) {
        setOtpError('Please enter the OTP sent to your email.');
        return;
      }
      if (otp.trim() !== '123456') { // Simulate OTP verification
        setOtpError('The OTP you entered is incorrect.');
        return;
      }
      setIsVerifying(true);
    // This would call: POST /api/auth/verify-otp
    // with payload: { email, otp}
    setTimeout(() => {
      setIsVerifying(false);
      const isValid = otp === '123456'; // Simulated backend validation
      if (isValid) {
        const displayName = extractDisplayName(email);
        if (setUser) setUser({ name: displayName, email});
        // Navigate after OTP
        const lower = email.toLowerCase();
        let target = 'chat';
        if (lower === 'admin@cfctech.com') target = 'admin';
        if (navigate) navigate('transition', { to: target, withFade: true });
      } else {
        setError('Invalid verification code. Please try again.');
        setOtp('');
      }
    }, 1500);
  };
    const handleBackToCredentials = () => {
      setStep('credentials');
      setOtp('');
      setError(null);
      setPassword('');
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
            {step === "credentials" && (
            <form onSubmit={handleRequestOtp} className="login-form">
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
              <PrimaryButton type="submit" disabled = {isVerifying}>
                {isVerifying ? 'Verifying...' : 'Continue'}
              </PrimaryButton>
              <p>
                  Don't have access? Contact your administrator.
                </p>
            </form>
            )}
            {step === "otp" && (
              <form onSubmit={handleVerifyOtp} className="otp-form">
                <h1>Verify your identity</h1> 
                <p> Verification code sent to {email}. 
                </p>
                <TextInput
                  label="Enter 6-digit code"
                  type = "text"
                  value={otp}
                  onChange={(e) => {setOtp(e.target.value); setOtpError('');}}
                  placeholder="000000"
                  error={otpError}
                />
                <div className = "otp-buttons">
                <PrimaryButton type="submit" disabled={isVerifying}>
                  {isVerifying ? 'Verifying...' : 'Verify & Sign in'}
                </PrimaryButton>
                <PrimaryButton type="button"  onClick={handleBackToCredentials}>
                  Back
                </PrimaryButton>
                <PrimaryButton type="button" className ="resend-button" onClick={() => handleSendOtp('email')}>
                  Didn't receive a code? Resend it.
                </PrimaryButton>
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
  window.CFC.Pages.LoginPage = LoginPage;
})();
