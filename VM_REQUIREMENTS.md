# CFC Chat-AI — Windows VM Hosting Requirements

## VM Specifications

| Spec | Minimum | Recommended |
|------|---------|-------------|
| **OS** | Windows Server 2022 | Windows Server 2022 |
| **vCPUs** | 2 | 4 |
| **RAM** | 8 GB | 16 GB |
| **Disk** | 60 GB | 100 GB |

> **Why higher specs than a typical web app?**  
> The application runs ML models (sentence-transformers for document search, Whisper for video transcription) and uses Docker with WSL2, which has additional memory and disk overhead compared to native Linux hosting.

### Suggested Azure VM Size

| VM Size | vCPUs | RAM |
|---------|-------|-----|
| **Standard_D2s_v5** | 2 | 8 GB |
| **Standard_D4s_v5** *(recommended)* | 4 | 16 GB |

---

## Software to Install Before Handoff

Please install the following on the VM before providing access to the development team:

### 1. WSL2 (Windows Subsystem for Linux)
Open **PowerShell as Administrator** and run:
```powershell
wsl --install
```
Then **reboot** the VM.

### 2. Docker Desktop
- Download from [docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop/)
- During setup, select **"Use WSL 2 based engine"**
- After install, open Docker Desktop and verify it's running (whale icon in system tray)

> **Note on licensing:** Docker Desktop requires a paid subscription ($5/user/month) for organizations with more than 250 employees or more than $10M in annual revenue (Docker, Inc. terms).

### 3. Git for Windows
- Download from [git-scm.com/download/win](https://git-scm.com/download/win)
- Use default settings during installation

### 4. IIS (Internet Information Services)
IIS is built into Windows Server but must be enabled:
1. Open **Server Manager** → **Add Roles and Features**
2. Select **Web Server (IIS)** → Install
3. Download and install these two IIS extensions:
   - [URL Rewrite Module](https://www.iis.net/downloads/microsoft/url-rewrite)
   - [Application Request Routing (ARR)](https://www.iis.net/downloads/microsoft/application-request-routing)

### 5. Firewall & Network
- Open **port 80** (HTTP) and **port 443** (HTTPS) in:
  - Windows Firewall (Inbound Rules)
  - Azure Network Security Group (NSG)
- Port **3389** (RDP) should already be open for remote access

---

## Access Required by Development Team

- **RDP access** to the VM (IP address + credentials or Azure Bastion)
- **Administrator privileges** on the VM (needed to configure IIS and Docker)
- The VM's **public IP address** or **domain name** (for CORS and DNS configuration)

---

## Pre-Handoff Checklist

Before providing VM access to the development team, please verify:

- [ ] VM provisioned with specs above (4 vCPUs, 16 GB RAM, 100 GB disk recommended)
- [ ] WSL2 installed (`wsl --install`) and VM rebooted
- [ ] Docker Desktop installed and running with WSL2 backend
- [ ] Git for Windows installed
- [ ] IIS enabled with URL Rewrite and ARR modules installed
- [ ] Ports 80 and 443 open in both Windows Firewall and Azure NSG
- [ ] RDP access credentials ready for the development team
