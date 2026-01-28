# Deployment Files

This directory contains configuration templates and files needed for deploying the CFC Chat-AI application to a production VM server.

## Files Included

### 1. `cfcchat.service`
**systemd service configuration file**

This file defines how the application runs as a system service on Linux.

**Usage**:
```bash
sudo cp cfcchat.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable cfcchat
sudo systemctl start cfcchat
```

### 2. `nginx-cfcchat.conf`
**Nginx reverse proxy configuration**

This file configures Nginx to serve your application and handle HTTPS.

**Usage**:
```bash
sudo cp nginx-cfcchat.conf /etc/nginx/sites-available/cfcchat
sudo ln -s /etc/nginx/sites-available/cfcchat /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

**Important**: Update `your-domain.com` with your actual domain name before deploying.

### 3. `.env.production`
**Production environment variables template**

This file contains all the environment variables needed for production deployment.

**Usage**:
1. Copy to the application root:
   ```bash
   cp .env.production ../.env
   ```

2. Edit with your actual credentials:
   ```bash
   nano ../.env
   ```

3. Update all values marked with `YOUR_*` placeholders

**Important**: This file contains sensitive credentials. Never commit it to version control!

## Quick Start

For detailed deployment instructions, see the main [DEPLOYMENT_GUIDE.md](../DEPLOYMENT_GUIDE.md) in the root directory.

## File Permissions

Set appropriate permissions for security:

```bash
# .env file (sensitive credentials)
chmod 600 .env

# Service file (root only)
sudo chmod 644 /etc/systemd/system/cfcchat.service

# Nginx config (root only)
sudo chmod 644 /etc/nginx/sites-available/cfcchat
```

## Customization

You may need to customize these files based on your specific requirements:

- **Port numbers**: The default is 8000 for the application
- **Domain names**: Update all instances of `your-domain.com`
- **File paths**: If you install the application in a different location
- **Worker count**: Based on your VM's CPU cores
- **File upload size**: Based on your needs (default: 100M)

## Verification

After deployment, verify everything is working:

```bash
# Check service is running
sudo systemctl status cfcchat

# Check Nginx is running
sudo systemctl status nginx

# Test API endpoint
curl http://localhost:8000/health

# Test through Nginx
curl https://your-domain.com/health
```

## Support

For questions or issues, refer to:
- [DEPLOYMENT_GUIDE.md](../DEPLOYMENT_GUIDE.md) - Complete deployment instructions
- [CLIENT_HANDOVER.md](../CLIENT_HANDOVER.md) - Client-facing documentation
- [QUICK_REFERENCE.md](../QUICK_REFERENCE.md) - Quick command reference

Contact Dan Bates from CFC Tech for production credentials and support.
