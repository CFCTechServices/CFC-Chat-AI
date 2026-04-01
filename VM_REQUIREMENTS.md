# CFC Chat-AI — Windows VM Hosting Requirements

## VM Specifications

| Spec | Minimum | Recommended |
|------|---------|-------------|
| **OS** | Windows 10 Pro | Windows 10 Pro |
| **vCPUs** | 2 | 4 |
| **RAM** | 8 GB | 16 GB |
| **Disk** | 60 GB | 100 GB |

> **Note:** Docker Desktop was evaluated and found incompatible with this VM due to Azure nested virtualization restrictions. The application runs natively using Python + NSSM Windows Service. No Docker is required.

---

## Software to Install Before Handoff

All steps below require **Administrator privileges** and only need to be done **once**.

### 1. Python 3.11
- Download from: https://python.org/downloads
- During install, check **"Add Python to PATH"**
- Verify: open PowerShell → `python --version`

### 2. Git for Windows
- Download from: https://git-scm.com/download/win
- Use default settings

### 3. ffmpeg
- Download a Windows build from: https://ffmpeg.org/download.html → Windows → gyan.dev builds → `ffmpeg-release-essentials.zip`
- Extract to `C:\ffmpeg`
- Add `C:\ffmpeg\bin` to the system PATH:
  - Search "Environment Variables" in Start → Edit the system environment variables → Environment Variables → System variables → Path → Edit → New → `C:\ffmpeg\bin`
- Verify: `ffmpeg -version`

### 4. Tesseract OCR (for scanned PDF support)
- Download installer from: https://github.com/UB-Mannheim/tesseract/wiki
- Install to default path (`C:\Program Files\Tesseract-OCR`)
- Add `C:\Program Files\Tesseract-OCR` to system PATH (same steps as above)
- Verify: `tesseract --version`

### 5. Poppler (for PDF to image conversion)
- Download from: https://github.com/oschwartz10612/poppler-windows/releases → latest `.zip`
- Extract to `C:\poppler`
- Add `C:\poppler\Library\bin` to system PATH
- Verify: `pdftoppm -v`

### 6. NSSM (Windows Service Manager)
- Download from: https://nssm.cc/download → `nssm-2.24.zip`
- Extract and copy `win64\nssm.exe` to `C:\Windows\System32\` (so it's on PATH)
- Verify: `nssm version`

### 7. IIS (Internet Information Services)
- Press **Win + R** → type `optionalfeatures` → Enter
- Check **Internet Information Services** (top-level)
- Also check: **IIS → World Wide Web Services → Application Development Features → CGI**
- Click OK

### 8. IIS URL Rewrite Module
- Download from: https://www.iis.net/downloads/microsoft/url-rewrite

### 9. IIS Application Request Routing (ARR)
- Download from: https://www.iis.net/downloads/microsoft/application-request-routing
- After install: open IIS Manager → click computer name → **Application Request Routing Cache** → **Server Proxy Settings** → check **Enable proxy** → Apply

### 10. Create App Folder and Grant Dev Access
```powershell
New-Item -ItemType Directory -Force -Path C:\cfcchat
# Grant the dev user Full Control:
# Right-click C:\cfcchat → Properties → Security → Edit → Add dev user → Full Control
```

---

## Firewall & Network
- Open **port 80** (HTTP) and **port 443** (HTTPS) in:
  - Windows Firewall (Inbound Rules)
  - Azure Network Security Group (NSG)

---

## Pre-Handoff Checklist

- [ ] Python 3.11 installed and on PATH
- [ ] Git for Windows installed
- [ ] ffmpeg installed and on PATH
- [ ] Tesseract installed and on PATH
- [ ] Poppler installed and on PATH
- [ ] NSSM installed (`nssm.exe` in System32)
- [ ] IIS enabled with CGI, URL Rewrite, ARR modules
- [ ] ARR proxy enabled in IIS Manager
- [ ] `C:\cfcchat` folder created with Full Control for dev user
- [ ] Ports 80 and 443 open in Windows Firewall and Azure NSG
- [ ] RDP access credentials ready for dev team
