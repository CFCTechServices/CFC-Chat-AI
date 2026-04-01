# CFC Chat-AI — Admin Setup Guide (Client Technical Team)

This document is for the **client's IT/technical team** who have administrator access to the Windows 10 Pro VM. All steps require admin privileges and only need to be done **once**.

> **Note:** Docker Desktop was evaluated and is not compatible with this VM due to Azure nested virtualization restrictions. The application runs natively using Python + NSSM (no Docker required).

---

## Step 1 — Install Python 3.11

1. Download from: https://python.org/downloads
2. Run the installer — check **"Add Python to PATH"** before clicking Install
3. Verify:
```powershell
python --version
```

---

## Step 2 — Install Git for Windows

1. Download from: https://git-scm.com/download/win
2. Run installer with default settings

---

## Step 3 — Install ffmpeg

1. Download a Windows build from https://ffmpeg.org/download.html → Windows → gyan.dev → `ffmpeg-release-essentials.zip`
2. Extract to `C:\ffmpeg`
3. Add to system PATH:
   - Search "**Environment Variables**" in Start menu
   - Click **Environment Variables** → under **System variables** → select **Path** → **Edit** → **New** → type `C:\ffmpeg\bin` → OK
4. Verify (new PowerShell window):
```powershell
ffmpeg -version
```

---

## Step 4 — Install Tesseract OCR

Required for processing scanned PDF documents.

1. Download installer from: https://github.com/UB-Mannheim/tesseract/wiki
2. Install to default path (`C:\Program Files\Tesseract-OCR`)
3. Add `C:\Program Files\Tesseract-OCR` to system PATH (same method as Step 3)
4. Verify:
```powershell
tesseract --version
```

---

## Step 5 — Install Poppler

Required for PDF-to-image conversion.

1. Download latest release from: https://github.com/oschwartz10612/poppler-windows/releases (`.zip` file)
2. Extract to `C:\poppler`
3. Add `C:\poppler\Library\bin` to system PATH
4. Verify:
```powershell
pdftoppm -v
```

---

## Step 6 — Install NSSM (Windows Service Manager)

NSSM runs the Python application as a Windows Service so it auto-starts and keeps running.

1. Download from: https://nssm.cc/download → `nssm-2.24.zip`
2. Extract the zip
3. Copy `win64\nssm.exe` into `C:\Windows\System32\`
4. Verify:
```powershell
nssm version
```

---

## Step 7 — Enable IIS

1. Press **Win + R** → type `optionalfeatures` → Enter
2. Check **Internet Information Services** (top-level box)
3. Also expand: **IIS → World Wide Web Services → Application Development Features** → check **CGI**
4. Click **OK** and wait for Windows to finish

---

## Step 8 — Install IIS URL Rewrite Module

1. Download from: https://www.iis.net/downloads/microsoft/url-rewrite
2. Run installer with default settings

---

## Step 9 — Install IIS ARR and Enable Proxy

1. Download from: https://www.iis.net/downloads/microsoft/application-request-routing
2. Run installer
3. Open **IIS Manager**
4. Click the **computer name** (top of left panel)
5. Double-click **Application Request Routing Cache**
6. Click **Server Proxy Settings** in the Actions pane
7. Check **Enable proxy** → click **Apply**

---

## Step 10 — Create App Folder and Grant Dev Access

```powershell
New-Item -ItemType Directory -Force -Path C:\cfcchat
```

Grant the dev user **Full Control** over `C:\cfcchat`:
1. Right-click `C:\cfcchat` → **Properties** → **Security** tab → **Edit**
2. Add the dev team's Windows username → check **Full Control** → OK

---

## Step 11 — Open Firewall Ports

- Open **port 80** (HTTP) and **port 443** (HTTPS) in:
  - Windows Firewall (Inbound Rules)
  - Azure Network Security Group (NSG)

---

## Summary Checklist

- [ ] Python 3.11 installed and on PATH
- [ ] Git for Windows installed
- [ ] ffmpeg installed and on PATH (`ffmpeg -version` works)
- [ ] Tesseract installed and on PATH (`tesseract --version` works)
- [ ] Poppler installed and on PATH (`pdftoppm -v` works)
- [ ] NSSM installed (`nssm version` works)
- [ ] IIS enabled (with CGI checked)
- [ ] IIS URL Rewrite module installed
- [ ] IIS ARR installed and proxy enabled in IIS Manager
- [ ] `C:\cfcchat` created with Full Control for dev user
- [ ] Ports 80 and 443 open in Windows Firewall and Azure NSG
- [ ] RDP access credentials ready for dev team

Once all items above are checked, the dev team can deploy and manage the application independently without admin access.
