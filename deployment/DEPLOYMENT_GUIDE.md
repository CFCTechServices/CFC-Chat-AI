# CFC Chat-AI - VM Deployment Guide

This guide provides complete instructions for deploying the CFC Chat-AI application on a Virtual Machine (VM) server.

## Table of Contents

- [System Requirements](#system-requirements)
- [Pre-Deployment Checklist](#pre-deployment-checklist)
- [Code Changes Required](#code-changes-required)
- [VM Setup Instructions](#vm-setup-instructions)
- [Application Installation](#application-installation)
- [Process Management with systemd](#process-management-with-systemd)
- [Reverse Proxy with Nginx](#reverse-proxy-with-nginx)
- [SSL/TLS Configuration](#ssltls-configuration)
- [Environment Variables](#environment-variables)
- [Firewall Configuration](#firewall-configuration)
- [Monitoring and Maintenance](#monitoring-and-maintenance)
- [Troubleshooting](#troubleshooting)

---

## System Requirements

### Minimum VM Specifications

- **OS**: Ubuntu 22.04 LTS or Ubuntu 24.04 LTS (recommended)
- **CPU**: 2 vCPUs (4+ recommended for better performance)
- **RAM**: 4 GB minimum (8 GB recommended)
- **Disk**: 40 GB SSD storage minimum
- **Network**: Public IP address with open ports 80 (HTTP) and 443 (HTTPS)

### Software Requirements

- Python 3.8 or higher (3.10+ recommended)
- Nginx web server
- SSL certificate (Let's Encrypt recommended)
- systemd (included in modern Linux distributions)

---

## Pre-Deployment Checklist

Before deploying to the VM, ensure you have:

- [ ] VM with Ubuntu installed and SSH access
- [ ] Domain name pointing to the VM's IP address (e.g., `chat.cfctech.com`)
- [ ] Required API credentials:
  - [ ] Pinecone API key and index details
  - [ ] OpenAI or Gemini API key
  - [ ] Supabase credentials (if using cloud storage)
- [ ] Git repository access or application files transferred to VM
- [ ] SSL certificate (can be obtained during setup with Let's Encrypt)

---

## Code Changes Required

### 1. CORS Configuration

**File**: `main.py` (lines 47-53)

**Current** (allows all origins - development only):
```python
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Configure appropriately for production
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
```

**Production** (restrict to your domain):
```python
app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "https://your-domain.com",
        "https://www.your-domain.com",
        # Add any other allowed origins
    ],
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE", "OPTIONS"],
    allow_headers=["*"],
)
```

### 2. Environment Configuration

**File**: `.env`

Update the `.env` file with production values:

```bash
# Change from development to production
ENV=production

# Update URLs to match your domain
FRONTEND_BASE_URL=https://your-domain.com
BACKEND_BASE_URL=https://your-domain.com

# Update CORS origins
CORS_ORIGINS=https://your-domain.com

# Enable referrer protection for production
ENABLE_REFERRER_PROTECTION=true
CFC_WEBSITE_URL=https://your-domain.com

# Production API keys (obtain from respective services)
PINECONE_API_KEY=your_production_pinecone_key
OPENAI_API_KEY=your_production_openai_key
GEMINI_API_KEY=your_production_gemini_key

# Session security (generate a strong secret)
SESSION_SECRET=your_long_random_secret_key_here
```

### 3. Security Headers (Recommended)

Add security middleware to `main.py` after the CORS middleware:

```python
from fastapi.middleware.trustedhost import TrustedHostMiddleware

# Add after CORS middleware
app.add_middleware(
    TrustedHostMiddleware, 
    allowed_hosts=["your-domain.com", "www.your-domain.com"]
)
```

### 4. Remove Debug/Reload Mode

**File**: `main.py` (lines 89-97)

**Current** (development):
```python
if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "main:app",
        host=settings.API_HOST,
        port=settings.API_PORT,
        reload=True,  # ← Remove this in production
        log_level="info"
    )
```

**Production** (this will be handled by systemd, so this section is less critical):
```python
if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "main:app",
        host=settings.API_HOST,
        port=settings.API_PORT,
        reload=False,
        log_level="warning",
        workers=4  # Multiple workers for production
    )
```

---

## VM Setup Instructions

### 1. Initial Server Setup

SSH into your VM:
```bash
ssh user@your-vm-ip-address
```

Update the system:
```bash
sudo apt update
sudo apt upgrade -y
```

### 2. Install Required Software

Install Python and dependencies:
```bash
sudo apt install -y python3 python3-pip python3-venv
sudo apt install -y nginx
sudo apt install -y git curl
```

Install build tools (required for some Python packages):
```bash
sudo apt install -y build-essential libssl-dev libffi-dev python3-dev
```

### 3. Create Application User

Create a dedicated user for the application:
```bash
sudo useradd -m -s /bin/bash cfcapp
sudo usermod -aG sudo cfcapp
```

### 4. Set Up Firewall

Configure UFW (Uncomplicated Firewall):
```bash
sudo ufw allow ssh
sudo ufw allow 'Nginx Full'
sudo ufw enable
```

---

## Application Installation

### 1. Clone/Transfer Application

Switch to the application user:
```bash
sudo su - cfcapp
```

Clone the repository (or transfer files):
```bash
cd /home/cfcapp
git clone <your-repository-url> CFC-Chat-AI
cd CFC-Chat-AI
```

Alternatively, if transferring via SCP:
```bash
# From your local machine:
scp -r /Users/shashreddy/Programming/CFC-Chat-AI user@vm-ip:/home/cfcapp/
```

### 2. Create Virtual Environment

```bash
cd /home/cfcapp/CFC-Chat-AI
python3 -m venv .venv
source .venv/bin/activate
```

### 3. Install Python Dependencies

```bash
pip install --upgrade pip
pip install -r requirements.txt
```

**Note**: Remove `pywin32` from `requirements.txt` as it's Windows-only:
```bash
# Edit requirements.txt and remove the line:
# pywin32
```

Or install with:
```bash
grep -v "pywin32" requirements.txt > requirements-linux.txt
pip install -r requirements-linux.txt
```

### 4. Configure Environment Variables

```bash
cp .env.example .env
nano .env  # or vim .env
```

Update all the values as described in the [Environment Variables](#environment-variables) section below.

### 5. Create Data Directories

```bash
mkdir -p data/documents/docx data/documents/doc
mkdir -p data/videos/transcripts
mkdir -p data/processed/content_repository
```

Set appropriate permissions:
```bash
chmod -R 755 data/
```

### 6. Test the Application

Test that the application runs:
```bash
source .venv/bin/activate
python main.py
```

If it starts successfully, press `Ctrl+C` to stop it. We'll use systemd for production.

---

## Process Management with systemd

Create a systemd service file to manage the application.

### 1. Create Service File

```bash
sudo nano /etc/systemd/system/cfcchat.service
```

Add the following content:

```ini
[Unit]
Description=CFC Chat-AI FastAPI Application
After=network.target

[Service]
Type=simple
User=cfcapp
Group=cfcapp
WorkingDirectory=/home/cfcapp/CFC-Chat-AI
Environment="PATH=/home/cfcapp/CFC-Chat-AI/.venv/bin"
ExecStart=/home/cfcapp/CFC-Chat-AI/.venv/bin/uvicorn main:app --host 0.0.0.0 --port 8000 --workers 4

# Restart policy
Restart=always
RestartSec=10

# Logging
StandardOutput=journal
StandardError=journal
SyslogIdentifier=cfcchat

[Install]
WantedBy=multi-user.target
```

### 2. Enable and Start Service

```bash
sudo systemctl daemon-reload
sudo systemctl enable cfcchat
sudo systemctl start cfcchat
```

### 3. Check Service Status

```bash
sudo systemctl status cfcchat
```

### 4. View Logs

```bash
# Follow logs in real-time
sudo journalctl -u cfcchat -f

# View last 100 lines
sudo journalctl -u cfcchat -n 100
```

---

## Reverse Proxy with Nginx

Configure Nginx as a reverse proxy to serve the application.

### 1. Create Nginx Configuration

```bash
sudo nano /etc/nginx/sites-available/cfcchat
```

Add the following configuration:

```nginx
server {
    listen 80;
    server_name your-domain.com www.your-domain.com;

    # Redirect to HTTPS (will be configured later with SSL)
    # For now, this serves HTTP

    client_max_body_size 100M;  # Allow large file uploads

    # Serve static frontend files
    location /ui {
        alias /home/cfcapp/CFC-Chat-AI/web;
        try_files $uri $uri/ /ui/index.html;
        add_header Cache-Control "public, max-age=3600";
    }

    # Proxy API requests to FastAPI
    location / {
        proxy_pass http://127.0.0.1:8000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # Increase timeouts for long-running requests
        proxy_connect_timeout 300;
        proxy_send_timeout 300;
        proxy_read_timeout 300;
        send_timeout 300;
    }

    # Serve uploaded images
    location /content/images {
        alias /home/cfcapp/CFC-Chat-AI/data/processed/content_repository;
        add_header Cache-Control "public, max-age=86400";
    }
}
```

### 2. Enable the Site

```bash
sudo ln -s /etc/nginx/sites-available/cfcchat /etc/nginx/sites-enabled/
sudo nginx -t  # Test configuration
sudo systemctl restart nginx
```

### 3. Test HTTP Access

Visit `http://your-domain.com` or `http://your-vm-ip` in a browser.

---

## SSL/TLS Configuration

Secure your application with HTTPS using Let's Encrypt.

### 1. Install Certbot

```bash
sudo apt install -y certbot python3-certbot-nginx
```

### 2. Obtain SSL Certificate

```bash
sudo certbot --nginx -d your-domain.com -d www.your-domain.com
```

Follow the prompts. Certbot will automatically update your Nginx configuration.

### 3. Verify Auto-Renewal

```bash
sudo certbot renew --dry-run
```

### 4. Updated Nginx Configuration

After certbot runs, your Nginx config will include:

```nginx
server {
    listen 443 ssl http2;
    server_name your-domain.com www.your-domain.com;

    ssl_certificate /etc/letsencrypt/live/your-domain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/your-domain.com/privkey.pem;
    
    # SSL configuration added by Certbot
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers on;
    
    # Rest of configuration same as above...
}

server {
    listen 80;
    server_name your-domain.com www.your-domain.com;
    return 301 https://$server_name$request_uri;
}
```

---

## Environment Variables

Complete `.env` configuration for production:

```bash
# Pinecone Configuration (REQUIRED)
PINECONE_API_KEY=your_production_pinecone_api_key
PINECONE_INDEX_NAME=cfc-animal-feed-chatbot-test
PINECONE_CLOUD=aws
PINECONE_REGION=us-east-1
PINECONE_INDEX_NAME_VIDEOS=cfc-videos
PINECONE_NAMESPACE=cfc-videos

# Embedding Model
EMBED_MODEL=all-MiniLM-L6-v2

# Supabase Storage (OPTIONAL)
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_ANON_KEY=your_supabase_anon_key
SUPABASE_BUCKET_VIDEOS=cfc-videos

# AI API Keys (At least one required for chat functionality)
OPENAI_API_KEY=your_production_openai_api_key
GEMINI_API_KEY=your_production_gemini_api_key
GEMINI_MODEL=gemini-2.5-flash

# Session Configuration
SESSION_SECRET=generate_a_long_random_string_here
SESSION_COOKIE_NAME=CFC_SESSION
SESSION_TTL_DAYS=7

# Production URLs (IMPORTANT: Update these)
FRONTEND_BASE_URL=https://your-domain.com
BACKEND_BASE_URL=https://your-domain.com
CORS_ORIGINS=https://your-domain.com

# Environment
ENV=production

# Security
ENABLE_REFERRER_PROTECTION=true
CFC_WEBSITE_URL=https://your-domain.com
```

**Generate SESSION_SECRET**:
```bash
python3 -c "import secrets; print(secrets.token_hex(32))"
```

---

## Firewall Configuration

Ensure proper firewall rules are in place:

```bash
# Allow SSH
sudo ufw allow ssh

# Allow HTTP and HTTPS
sudo ufw allow 'Nginx Full'

# Enable firewall
sudo ufw enable

# Check status
sudo ufw status
```

---

## Monitoring and Maintenance

### Application Logs

View application logs:
```bash
# Real-time logs
sudo journalctl -u cfcchat -f

# Logs from last hour
sudo journalctl -u cfcchat --since "1 hour ago"

# Logs with errors only
sudo journalctl -u cfcchat -p err
```

### Nginx Logs

```bash
# Access logs
sudo tail -f /var/log/nginx/access.log

# Error logs
sudo tail -f /var/log/nginx/error.log
```

### Restart Services

```bash
# Restart application
sudo systemctl restart cfcchat

# Restart Nginx
sudo systemctl restart nginx

# Restart both
sudo systemctl restart cfcchat nginx
```

### Update Application

```bash
# Switch to app user
sudo su - cfcapp
cd /home/cfcapp/CFC-Chat-AI

# Pull latest changes
git pull origin main

# Activate virtual environment
source .venv/bin/activate

# Update dependencies
pip install -r requirements.txt

# Restart service
exit  # Exit back to regular user
sudo systemctl restart cfcchat
```

### Disk Space Monitoring

```bash
# Check disk usage
df -h

# Check largest directories
du -sh /home/cfcapp/CFC-Chat-AI/data/*

# Clean up old videos/documents if needed
# (be careful with this - back up first!)
```

---

## Troubleshooting

### Application Won't Start

1. Check systemd status:
   ```bash
   sudo systemctl status cfcchat
   ```

2. Check logs:
   ```bash
   sudo journalctl -u cfcchat -n 100
   ```

3. Verify Python dependencies:
   ```bash
   sudo su - cfcapp
   cd /home/cfcapp/CFC-Chat-AI
   source .venv/bin/activate
   pip list
   ```

4. Test manually:
   ```bash
   python main.py
   ```

### Nginx Errors

1. Test configuration:
   ```bash
   sudo nginx -t
   ```

2. Check error logs:
   ```bash
   sudo tail -f /var/log/nginx/error.log
   ```

3. Verify permissions:
   ```bash
   ls -la /home/cfcapp/CFC-Chat-AI/web/
   ```

### Can't Upload Files

1. Check file size limits in Nginx configuration (`client_max_body_size`)
2. Verify directory permissions:
   ```bash
   ls -la /home/cfcapp/CFC-Chat-AI/data/
   ```
3. Ensure sufficient disk space:
   ```bash
   df -h
   ```

### Pinecone Connection Errors

1. Verify API key in `.env`
2. Check network connectivity:
   ```bash
   curl https://api.pinecone.io/
   ```
3. Review application logs for specific error messages

### SSL Certificate Issues

1. Renew certificate manually:
   ```bash
   sudo certbot renew
   ```

2. Check certificate expiry:
   ```bash
   sudo certbot certificates
   ```

3. Verify Nginx SSL configuration:
   ```bash
   sudo nginx -t
   ```

---

## Additional Production Considerations

### 1. Database Backup (if using Supabase)

Set up regular backups of Supabase data or configure Supabase backup policies.

### 2. Rate Limiting

Consider implementing rate limiting at the Nginx level:

```nginx
# Add to nginx config
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=10r/s;

location /api/ {
    limit_req zone=api_limit burst=20 nodelay;
    # ... rest of config
}
```

### 3. Monitoring Tools

Consider installing:
- **Prometheus + Grafana**: Application metrics
- **fail2ban**: Intrusion prevention
- **Netdata**: Real-time system monitoring

```bash
# Example: Install Netdata
bash <(curl -Ss https://my-netdata.io/kickstart.sh)
```

### 4. Automated Backups

Create a backup script:

```bash
#!/bin/bash
# /home/cfcapp/backup.sh

BACKUP_DIR="/home/cfcapp/backups"
DATE=$(date +%Y%m%d_%H%M%S)

# Create backup directory
mkdir -p $BACKUP_DIR

# Backup data directory
tar -czf $BACKUP_DIR/data_$DATE.tar.gz /home/cfcapp/CFC-Chat-AI/data/

# Keep only last 7 days of backups
find $BACKUP_DIR -name "data_*.tar.gz" -mtime +7 -delete
```

Add to crontab:
```bash
crontab -e
# Add: Daily backup at 2 AM
0 2 * * * /home/cfcapp/backup.sh
```

---

## Summary of Changes from Local to VM

| Aspect | Local Development | Production VM |
|--------|------------------|---------------|
| **Host** | `localhost:8000` | `https://your-domain.com` |
| **CORS** | Allow all origins (`*`) | Specific domain only |
| **Environment** | `ENV=development` | `ENV=production` |
| **Reload** | `reload=True` | `reload=False`, multiple workers |
| **Process Manager** | Run manually | systemd service |
| **Web Server** | Built-in uvicorn | Nginx reverse proxy |
| **SSL/TLS** | Not needed | Let's Encrypt certificate |
| **Firewall** | Not configured | UFW with specific rules |
| **Logging** | Console output | systemd journald |
| **Secrets** | `.env` file in repo | Secure `.env` file on VM |
| **Static Files** | Served by FastAPI | Served by Nginx |

---

## Next Steps

1. ✅ Set up VM with required specifications
2. ✅ Point domain name to VM IP
3. ✅ Complete VM setup and install dependencies
4. ✅ Transfer application code and install
5. ✅ Configure environment variables
6. ✅ Set up systemd service
7. ✅ Configure Nginx reverse proxy
8. ✅ Obtain SSL certificate
9. ✅ Test application functionality
10. ✅ Set up monitoring and backups

---

**Questions or Issues?**

Contact Dan Bates from CFC Tech for assistance with:
- API credentials (Pinecone, Supabase)
- Domain configuration
- Production requirements
