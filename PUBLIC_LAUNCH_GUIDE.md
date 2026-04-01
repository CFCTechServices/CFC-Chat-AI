# Making CFC Chat AI Publicly Accessible

This document outlines what the **client's IT team** and the **development team** each need to do to make the application available on the public internet (no VPN required).

---

## Client IT Team Responsibilities

### 1. Register a Domain Name
Purchase a domain (e.g. `chat.cfctech.com`) from any registrar (GoDaddy, Namecheap, Google Domains, etc.).

### 2. Point the Domain to the VM
In the domain registrar's DNS settings, create an **A record**:
- Host: `@` or `chat` (depending on desired URL)
- Value: the VM's **public IP address**

> The public IP can be found in the Azure Portal under the VM's **Networking** settings.

### 3. Open the Firewall to the Internet
In the Azure Portal, update the VM's **Network Security Group (NSG)**:
- Add an **Inbound rule** for port `80` (HTTP) from source `Any`
- Add an **Inbound rule** for port `443` (HTTPS) from source `Any`

Also ensure the Windows Firewall on the VM allows ports 80 and 443 (port 80 was already opened — port 443 will be needed for HTTPS).

### 4. Install an SSL Certificate (HTTPS)
On the VM, download and run **Win-ACME** (free Let's Encrypt client for IIS):
1. Download from: https://www.win-acme.com/
2. Run as Administrator and follow prompts to issue a certificate for the domain
3. Win-ACME automatically installs the cert into IIS and sets up auto-renewal

---

## Development Team Responsibilities

### 1. Update Environment Variables on the VM
```powershell
notepad C:\cfcchat\.env
```
Change:
```
CORS_ORIGINS=https://your-domain.com
FRONTEND_BASE_URL=https://your-domain.com
```
Then:
```powershell
nssm restart CFC-ChatAI
```

### 2. Update Supabase Auth Settings
In the Supabase Dashboard → **Authentication → URL Configuration**:
- **Site URL**: `https://your-domain.com`
- **Redirect URLs**: add `https://your-domain.com/*`

> ⚠️ **Important:** Supabase verification and password-reset emails currently link back to `http://localhost:8000` because the app is still in active development. Until the Supabase Site URL is updated to the production domain, email-based flows (sign-up verification, password reset) will **not work** for users accessing the VM. This is intentional during development and must be updated as part of go-live.

### 3. Update the Team Access Guide
Update `TEAM_ACCESS_GUIDE.md` with the public URL so the team no longer needs VPN.

---

## Summary Checklist

### Client IT Team
- [ ] Domain registered and DNS A record pointing to the VM's public IP
- [ ] Azure NSG inbound rules open for ports 80 and 443
- [ ] SSL certificate installed via Win-ACME

### Development Team
- [ ] `.env` updated on VM (`CORS_ORIGINS`, `FRONTEND_BASE_URL` → new domain)
- [ ] `nssm restart CFC-ChatAI` run on VM
- [ ] Supabase Auth redirect URLs updated
- [ ] `TEAM_ACCESS_GUIDE.md` updated with public URL

---

## After Go-Live

- Users can access the app at `https://your-domain.com` from any browser, no VPN needed
- Win-ACME automatically renews the SSL certificate every 60 days
- The development team can still access the VM via RDP + VPN for maintenance
