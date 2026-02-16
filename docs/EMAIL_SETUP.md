# Email Invitation Setup Guide

This document explains how to configure email invitations for the CFC Animal Feed Software Chatbot application.

## Overview

The admin invite feature allows administrators to send email invitations to new users. The system uses [Resend](https://resend.com) as the email service provider.

## Current Status

✅ **Invitation System**: Fully functional - creates invite codes and stores them in the database  
⚠️ **Email Sending**: Optional - requires domain verification for production use

## Quick Setup

### Option 1: Disable Email Sending (Development/Testing)

If you don't need email sending or are still setting up Resend:

1. **Update `.env`:**
   ```env
   ENABLE_EMAIL_INVITES=false
   ```

2. **How it works:**
   - Invitations are created in the database
   - No emails are sent
   - The invite URL is printed to the console
   - Manually share the URL with the user

### Option 2: Enable Email Sending (Production)

To send actual emails to any recipient:

**Prerequisites:**
- Resend account (free tier available)
- Verified domain

**Setup Steps:**

#### Step 1: Create Resend Account
1. Go to [resend.com](https://resend.com)
2. Sign up for a free account
3. Generate an API key from the dashboard

#### Step 2: Verify Your Domain
1. Go to **Domains** in Resend dashboard
2. Click **Add Domain**
3. Enter your domain (e.g., `yourdomain.com` or `mail.yourdomain.com`)
4. Add the DNS records shown by Resend to your domain provider:
   - **SPF** (TXT record)
   - **DKIM** (TXT record)
   - **DMARC** (TXT record) - optional but recommended
5. Wait for verification (usually 5-15 minutes)
6. Confirm the domain shows as "Verified" in Resend

#### Step 3: Update Configuration

1. **Update `.env`:**
   ```env
   # Resend Configuration
   RESEND_API_KEY=re_xxxxxxxxxxxxxxxxxxxxxxxxxx
   FRONTEND_BASE_URL=https://yourdomain.com
   ENABLE_EMAIL_INVITES=true
   ```

2. **Update email sender in `app/services/email_service.py`:**
   ```python
   # Line 132
   "from": "Your App Name <noreply@yourdomain.com>",
   ```
   Replace with your verified domain email.

#### Step 4: Test
Run the test script:
```bash
python test_admin_invite.py
```

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `RESEND_API_KEY` | No* | None | API key from Resend dashboard |
| `FRONTEND_BASE_URL` | Yes | `http://localhost:3000` | Base URL for invite links |
| `ENABLE_EMAIL_INVITES` | No | `true` | Enable/disable email sending |

*Required only if `ENABLE_EMAIL_INVITES=true`

## Troubleshooting

### Issue: "You can only send testing emails to your own email address"

**Cause:** Domain not verified in Resend  
**Solution:** 
- Either verify a domain (see Step 2 above)
- Or only send invites to your account email
- Or disable emails with `ENABLE_EMAIL_INVITES=false`

### Issue: Emails not being received

**Check:**
1. Spam/junk folder
2. Domain verification status in Resend
3. DNS records are correctly configured
4. API key is valid
5. Check Resend dashboard logs

### Issue: API key invalid

**Solution:**
1. Verify `RESEND_API_KEY` in `.env` is correct
2. Regenerate API key in Resend dashboard if needed
3. Restart the server after updating `.env`

## Testing Limitations

### Free Tier (No Domain)
- ✅ Can send to account owner's email
- ❌ Cannot send to other emails
- ✅ Can create invitations (emails disabled)

### Verified Domain
- ✅ Can send to any email address
- ✅ Professional sender address
- ✅ Better deliverability

## File References

- Email service: [`app/services/email_service.py`](../app/services/email_service.py)
- Admin endpoint: [`app/api/endpoints/admin.py`](../app/api/endpoints/admin.py)
- Configuration: [`app/config.py`](../app/config.py)
- Test script: [`test_admin_invite.py`](../test_admin_invite.py)

## Future Improvements

Consider these enhancements for production:

1. **Email Templates**: Create multiple templates for different email types
2. **Queue System**: Use background tasks for email sending
3. **Retry Logic**: Implement exponential backoff for failed emails
4. **Email Tracking**: Track open rates and clicks
5. **Customization**: Allow admins to customize email content
6. **Alternative Providers**: Support SendGrid, Mailgun, etc.

## Support

For issues with:
- **Resend**: Visit [resend.com/docs](https://resend.com/docs)
- **DNS Setup**: Contact your domain provider
- **Application**: Check server logs and test scripts
