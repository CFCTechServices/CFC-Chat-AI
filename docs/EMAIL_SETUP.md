# Signup, confirmation email, and password recovery

In the .NET app, **Admin → Users → Invite** creates an invitation record for an email address; it does not send an invitation email or expose an invite code. Share the approved site URL with the invitee and ask them to register with that same email. The UI checks invitation eligibility before calling Supabase Auth for signup. Verify the target project's signup/invitation and profile rules before relying on an invitation-only policy; a UI check alone is not a database or Auth security boundary.

Supabase Auth sends signup confirmation (when enabled) and password recovery emails. The .NET app does not use `RESEND_API_KEY`, `ENABLE_EMAIL_INVITES`, or `FRONTEND_BASE_URL`.

## Configure and verify

1. In Supabase **Authentication → URL Configuration**, set **Site URL** to the deployed Blazor origin. Add the actual frontend callbacks to **Redirect URLs**: `https://your-site.example/login?signup=confirm` and `https://your-site.example/reset-password`. For local HTTP development, allow `http://localhost:5077/login?signup=confirm` and `http://localhost:5077/reset-password` in the development project. Use the actual origin and port if they differ.
2. Set `PASSWORD_RESET_REDIRECT_URL` in the .NET API's environment to the full `/reset-password` URL and restart the API. Blazor derives the signup callback from the browser's current origin. Existing environment variables take precedence over `.env`.
3. Review Supabase's confirmation setting, [email templates](https://supabase.com/docs/guides/auth/auth-email-templates), and [SMTP configuration](https://supabase.com/docs/guides/auth/auth-smtp). Use approved SMTP for real delivery; the built-in sender has limits and is not intended for production. Keep SMTP credentials in Supabase, not in the repository.
4. In a disposable project, invite a test email, register at `/login`, confirm if required, and sign in. Request password recovery and verify the link opens `/reset-password` at the intended origin and the updated password works. The API intentionally gives a generic response to recovery requests; success text alone is not proof of email delivery.

If signup is denied, check the exact invited email and invitation expiry. If links lead to the wrong origin, verify the redirect allowlist and template, the API reset URL, and which Supabase project the app is using. If mail never arrives, check Supabase Auth/SMTP logs, rate limits, delivery restrictions and spam folders. Do not include email links or tokens in logs or bug reports.

The legacy Resend implementation is preserved in [`.archive/`](../.archive/README.md) for history. Do not configure it for Blazor.
