# Accessing the CFC Chat AI — Team & Client Guide

## How to Access the Website

The CFC Chat AI is currently running on a private VM. To access it you need to connect via VPN first.

### Step 1 — Install NetExtender (one-time)
Download SonicWall NetExtender from:
> https://www.sonicwall.com/products/remote-access/vpn-clients

### Step 2 — Connect to VPN
Open NetExtender and log in with the credentials provided by the client's IT team.

### Step 3 — Open the Website
Once connected to VPN, open any browser and go to:

> **http://192.168.201.211**

You should see the CFC Chat AI login page.

---

## Why It Is Not on the Internet Yet

The website runs on a **private VM** inside the client's internal network. Its IP address (`192.168.201.211`) is only reachable through the VPN — it is behind a firewall and cannot be accessed from the open internet.

This is intentional while the application is in testing. It keeps the system secure until it is ready for a public launch.

---

## What Needs to Happen to Make It Publicly Accessible

1. **Register a domain name** (e.g. `chat.cfctech.com`) and point it to the VM's public IP address
2. **Open the firewall** to allow public traffic on ports 80 (HTTP) and 443 (HTTPS)
3. **Set up HTTPS** with an SSL certificate for the domain
4. **Update the application configuration** (two environment variables) to reflect the new domain

Once these steps are completed, anyone can access the website from their browser without needing a VPN.

---

## Known Limitations (While in Development)

> **Email verification and password reset links will not work correctly for users accessing the VM.**
>
> Supabase currently sends emails with links pointing to `http://localhost:8000` because the app is still in active development. This will be fixed when the application goes live with a public domain — updating the Supabase Site URL is part of the go-live checklist in `PUBLIC_LAUNCH_GUIDE.md`.
>
> For now, user accounts should be created and managed directly by the admin.

