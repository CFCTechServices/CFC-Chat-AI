# CFC Chat-AI — Admin Setup Guide (Client Technical Team)

This document is intended for the **client's IT/technical team** who have administrator access to the Windows 10 Pro VM (`192.168.33.211`). All steps below require admin privileges and only need to be done **once**.

---

## Step 1 — Enable WSL2 (Windows Subsystem for Linux)

Open **PowerShell as Administrator** and run:

```powershell
wsl --install
```

**Reboot the VM when prompted.** WSL2 is required for Docker Desktop to function on Windows 10.

---

## Step 2 — Install Docker Desktop

1. Download from: https://www.docker.com/products/docker-desktop/
2. Run the installer — when asked, select **"Use WSL 2 based engine"**
3. After install, open Docker Desktop and wait until the whale icon in the taskbar shows **"Docker Desktop is running"**
4. In Docker Desktop settings, confirm **WSL 2 based engine** is enabled under **General**

> **Licensing note:** Docker Desktop requires a paid license for companies with more than 250 employees or more than $10M annual revenue ($5/user/month). Please confirm this with your organization.

---

## Step 3 — Install Git for Windows

1. Download from: https://git-scm.com/download/win
2. Run the installer with all default settings

---

## Step 4 — Enable IIS (Internet Information Services)

1. Press **Windows + R** → type `optionalfeatures` → press Enter
2. In the list, expand **Internet Information Services** and check the top-level box
3. Also expand: **Internet Information Services → World Wide Web Services → Application Development Features** → check **CGI**
4. Click **OK** and wait for Windows to finish installing

---

## Step 5 — Install IIS URL Rewrite Module

1. Download from: https://www.iis.net/downloads/microsoft/url-rewrite
2. Run the installer with default settings

---

## Step 6 — Install IIS Application Request Routing (ARR)

1. Download from: https://www.iis.net/downloads/microsoft/application-request-routing
2. Run the installer with default settings
3. Open **IIS Manager**
4. Click the **computer name** at the top of the left panel
5. Double-click **Application Request Routing Cache**
6. In the right Actions panel, click **Server Proxy Settings**
7. Check **Enable proxy** → click **Apply**

---

## Step 7 — Create a Folder for the Application

```powershell
New-Item -ItemType Directory -Force -Path C:\cfcchat
```

Grant the development team user **Full Control** over `C:\cfcchat` so they can clone and run the app without admin rights:

1. Right-click `C:\cfcchat` → **Properties** → **Security** tab → **Edit**
2. Add the dev team's Windows user account → check **Full Control** → **OK**

---

## Step 8 — Grant Docker Access to Dev User

Add the dev team's Windows user to the **`docker-users`** local group so they can run Docker without admin rights:

```powershell
Add-LocalGroupMember -Group "docker-users" -Member "<dev-username>"
```

Replace `<dev-username>` with the actual Windows username of the developer.

---

## Summary Checklist

- [ ] WSL2 installed (`wsl --install`) and VM rebooted
- [ ] Docker Desktop installed (WSL2 backend), running and confirmed in system tray
- [ ] Git for Windows installed
- [ ] IIS enabled via `optionalfeatures` (with CGI checked)
- [ ] IIS URL Rewrite module installed
- [ ] IIS ARR module installed and proxy enabled in IIS Manager
- [ ] `C:\cfcchat` folder created with Full Control for the dev user
- [ ] Dev user added to the `docker-users` local group

Once all the above are complete, the development team can deploy and manage the application entirely on their own without needing admin access again.
