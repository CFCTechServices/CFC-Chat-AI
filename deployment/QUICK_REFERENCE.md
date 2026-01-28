# CFC Chat-AI Quick Reference Guide

## 🚀 Quick Commands Reference

### Service Management
```bash
# Start application
sudo systemctl start cfcchat

# Stop application
sudo systemctl stop cfcchat

# Restart application
sudo systemctl restart cfcchat

# Check status
sudo systemctl status cfcchat

# Enable auto-start on boot
sudo systemctl enable cfcchat
```

### View Logs
```bash
# Follow application logs in real-time
sudo journalctl -u cfcchat -f

# View last 100 lines
sudo journalctl -u cfcchat -n 100

# View logs from last hour
sudo journalctl -u cfcchat --since "1 hour ago"

# View only errors
sudo journalctl -u cfcchat -p err

# Nginx access logs
sudo tail -f /var/log/nginx/access.log

# Nginx error logs
sudo tail -f /var/log/nginx/error.log
```

### Nginx Management
```bash
# Test configuration
sudo nginx -t

# Reload configuration (no downtime)
sudo nginx -s reload

# Restart Nginx
sudo systemctl restart nginx

# Check status
sudo systemctl status nginx
```

### System Monitoring
```bash
# Check disk space
df -h

# Check memory usage
free -h

# Check CPU usage
top
# or
htop

# Check process
ps aux | grep uvicorn

# Check open ports
sudo netstat -tulpn | grep LISTEN
```

### SSL Certificate Management
```bash
# Check certificate expiry
sudo certbot certificates

# Renew certificates manually
sudo certbot renew

# Test renewal process
sudo certbot renew --dry-run
```

### Application Updates
```bash
# Switch to app user
sudo su - cfcapp

# Navigate to application
cd /home/cfcapp/CFC-Chat-AI

# Pull latest code
git pull origin main

# Activate virtual environment
source .venv/bin/activate

# Update dependencies
pip install -r requirements.txt

# Exit to regular user
exit

# Restart application
sudo systemctl restart cfcchat
```

### Backup Data
```bash
# Backup data directory
sudo tar -czf /tmp/cfc-backup-$(date +%Y%m%d).tar.gz \
  /home/cfcapp/CFC-Chat-AI/data/

# Copy backup to safe location
scp /tmp/cfc-backup-*.tar.gz user@backup-server:/backups/
```

### Restore Data
```bash
# Extract backup
sudo tar -xzf /path/to/backup.tar.gz -C /

# Restart application
sudo systemctl restart cfcchat
```

---

## 🌐 Important URLs

| Service | URL | Description |
|---------|-----|-------------|
| **Web UI** | `https://your-domain.com/ui` | Main chat interface |
| **API Docs** | `https://your-domain.com/docs` | Interactive API documentation |
| **Health Check** | `https://your-domain.com/health` | Application health status |
| **Vector Store Stats** | `https://your-domain.com/visibility/vector-store` | Pinecone index information |

---

## 📁 Important File Locations

| Item | Path | Purpose |
|------|------|---------|
| Application Root | `/home/cfcapp/CFC-Chat-AI/` | Main application directory |
| Environment Variables | `/home/cfcapp/CFC-Chat-AI/.env` | Configuration (⚠️ SENSITIVE) |
| Service Config | `/etc/systemd/system/cfcchat.service` | systemd service definition |
| Nginx Config | `/etc/nginx/sites-available/cfcchat` | Web server configuration |
| SSL Certificates | `/etc/letsencrypt/live/your-domain.com/` | SSL/TLS certificates |
| Application Logs | `journalctl -u cfcchat` | systemd journal |
| Nginx Access Logs | `/var/log/nginx/access.log` | Web server access logs |
| Nginx Error Logs | `/var/log/nginx/error.log` | Web server error logs |
| Uploaded Documents | `/home/cfcapp/CFC-Chat-AI/data/documents/` | User uploads |
| Processed Content | `/home/cfcapp/CFC-Chat-AI/data/processed/` | Processed files |
| Videos | `/home/cfcapp/CFC-Chat-AI/data/videos/` | Uploaded videos |

---

## 🔥 Emergency Troubleshooting

### Application Won't Start

1. Check logs:
   ```bash
   sudo journalctl -u cfcchat -n 50
   ```

2. Test manually:
   ```bash
   sudo su - cfcapp
   cd /home/cfcapp/CFC-Chat-AI
   source .venv/bin/activate
   python main.py
   ```

3. Check environment variables:
   ```bash
   cat /home/cfcapp/CFC-Chat-AI/.env | grep -v "^$" | grep -v "^#"
   ```

### Website Not Loading

1. Check Nginx:
   ```bash
   sudo systemctl status nginx
   sudo nginx -t
   ```

2. Check if application is running:
   ```bash
   curl http://localhost:8000/health
   ```

3. Check firewall:
   ```bash
   sudo ufw status
   ```

### Upload Failing

1. Check disk space:
   ```bash
   df -h
   ```

2. Check directory permissions:
   ```bash
   ls -la /home/cfcapp/CFC-Chat-AI/data/
   ```

3. Check Nginx file size limit (in `/etc/nginx/sites-available/cfcchat`):
   ```
   client_max_body_size 100M;
   ```

### Slow Performance

1. Check CPU and memory:
   ```bash
   top
   ```

2. Check disk I/O:
   ```bash
   iostat -x 1 5
   ```

3. Increase workers (edit `/etc/systemd/system/cfcchat.service`):
   ```ini
   ExecStart=/home/cfcapp/CFC-Chat-AI/.venv/bin/uvicorn main:app --host 0.0.0.0 --port 8000 --workers 8
   ```
   Then:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl restart cfcchat
   ```

---

## 🔐 Security Checklist

- [ ] `.env` file permissions set to 600 (only owner can read/write)
- [ ] SSH access via keys only (password disabled)
- [ ] Firewall (UFW) enabled with minimal ports open
- [ ] SSL certificate valid and auto-renewing
- [ ] CORS configured for specific domain only
- [ ] Regular security updates applied
- [ ] Sensitive logs not exposed publicly
- [ ] API keys rotated periodically

---

## 📊 Monitoring Checklist

### Daily (Automated Alert Recommended)
- [ ] Application is running (`systemctl status cfcchat`)
- [ ] No critical errors in logs
- [ ] Disk space > 20% free

### Weekly
- [ ] Review application logs for errors
- [ ] Check API usage and costs
- [ ] Verify backups are working

### Monthly
- [ ] Update system packages
- [ ] Review SSL certificate expiry (auto-renews, but verify)
- [ ] Check for application updates
- [ ] Review security logs

---

## 🆘 Who to Contact

| Issue | Contact |
|-------|---------|
| Pinecone issues | Dan Bates @ CFC Tech |
| Supabase issues | Dan Bates @ CFC Tech |
| Business requirements | Dan Bates @ CFC Tech |
| OpenAI API issues | https://help.openai.com/ |
| Gemini API issues | https://support.google.com/ |
| SSL certificate issues | Let's Encrypt Community Forums |
| VM/hosting issues | Your hosting provider |

---

## 📱 Quick Health Check Script

Save this as `/home/cfcapp/health_check.sh`:

```bash
#!/bin/bash
echo "=== CFC Chat-AI Health Check ==="
echo ""
echo "1. Service Status:"
systemctl is-active cfcchat
echo ""
echo "2. Nginx Status:"
systemctl is-active nginx
echo ""
echo "3. Disk Space:"
df -h / | tail -1 | awk '{print $5 " used"}'
echo ""
echo "4. Memory:"
free -h | grep Mem | awk '{print $3 "/" $2 " used"}'
echo ""
echo "5. Application Responding:"
curl -s -o /dev/null -w "%{http_code}" http://localhost:8000/health
echo ""
echo "6. SSL Certificate:"
certbot certificates 2>&1 | grep Expiry | head -1
echo ""
echo "=== End of Health Check ==="
```

Make it executable:
```bash
chmod +x /home/cfcapp/health_check.sh
```

Run it:
```bash
/home/cfcapp/health_check.sh
```

---

## 🎯 Performance Tuning Tips

### Increase Workers
Edit `/etc/systemd/system/cfcchat.service` and change workers based on CPU cores:
```
--workers 4  # Formula: (2 x CPU cores) + 1
```

### Enable Nginx Caching
Add to Nginx config:
```nginx
proxy_cache_path /var/cache/nginx levels=1:2 keys_zone=api_cache:10m;

location /api/ {
    proxy_cache api_cache;
    proxy_cache_valid 200 5m;
    # ... rest of config
}
```

### Monitor Resource Usage
```bash
# Real-time monitoring
htop

# Identify expensive processes
ps aux --sort=-%cpu | head -10
ps aux --sort=-%mem | head -10
```

---

## 📞 Support Resources

- **Deployment Guide**: `DEPLOYMENT_GUIDE.md`
- **Client Handover**: `CLIENT_HANDOVER.md`
- **Project README**: `README.md`
- **API Documentation**: `https://your-domain.com/docs`
- **FastAPI Docs**: https://fastapi.tiangolo.com/
- **Nginx Docs**: https://nginx.org/en/docs/
- **Ubuntu Server Guide**: https://ubuntu.com/server/docs
