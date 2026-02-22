(() => {
  const { Card } = window.CFC.Primitives;
  const { Toggle } = window.CFC.Admin;
  const { useUser } = window.CFC.UserContext;
  const { useState, useEffect, useCallback } = React;

  function SettingsTab() {
    const { session } = useUser();
    const [settings, setSettings] = useState(null);
    const [loading, setLoading] = useState(true);
    const [healthStatus, setHealthStatus] = useState(null);
    const [responseTime, setResponseTime] = useState(null);

    const authHeaders = {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${session?.access_token}`,
    };

    // Fetch settings on mount
    useEffect(() => {
      fetch('/api/admin/settings', { headers: authHeaders })
        .then(res => res.json())
        .then(data => setSettings(data))
        .catch(err => console.error('Failed to load settings:', err))
        .finally(() => setLoading(false));
    }, []);

    // Fetch health on mount
    useEffect(() => {
      const start = performance.now();
      fetch('/api/health')
        .then(res => {
          const elapsed = Math.round(performance.now() - start);
          setResponseTime(elapsed);
          setHealthStatus(res.ok ? 'healthy' : 'unhealthy');
        })
        .catch(() => setHealthStatus('unreachable'));
    }, []);

    const handleToggle = useCallback((key, value) => {
      // Optimistic update
      setSettings(prev => ({ ...prev, [key]: value }));

      fetch('/api/admin/settings', {
        method: 'PATCH',
        headers: authHeaders,
        body: JSON.stringify({ [key]: value }),
      })
        .then(res => {
          if (!res.ok) throw new Error('Failed to save');
          return res.json();
        })
        .then(data => setSettings(data))
        .catch(err => {
          console.error('Failed to update setting:', err);
          // Revert on failure
          setSettings(prev => ({ ...prev, [key]: !value }));
        });
    }, [session]);

    if (loading) {
      return <div className="tab-content"><p>Loading settings...</p></div>;
    }

    return (
      <div className="tab-content">
        <Card className="settings-card">
          <h2>System Settings</h2>

          <div className="settings-list">
            <div className="setting-item">
              <div className="setting-info">
                <h3 className="setting-title">Auto-approve uploads</h3>
                <p className="setting-description">Automatically make uploaded content visible</p>
              </div>
              <Toggle
                checked={settings?.auto_approve_uploads ?? false}
                onChange={v => handleToggle('auto_approve_uploads', v)}
              />
            </div>

            <div className="setting-item">
              <div className="setting-info">
                <h3 className="setting-title">Email notifications</h3>
                <p className="setting-description">Send alerts for ingestion completion</p>
              </div>
              <Toggle
                checked={settings?.email_notifications ?? true}
                onChange={v => handleToggle('email_notifications', v)}
              />
            </div>

            <div className="setting-item">
              <div className="setting-info">
                <h3 className="setting-title">Maintenance mode</h3>
                <p className="setting-description">Restrict access for system maintenance</p>
              </div>
              <Toggle
                checked={settings?.maintenance_mode ?? false}
                onChange={v => handleToggle('maintenance_mode', v)}
              />
            </div>
          </div>
        </Card>

        <div className="api-health-section">
          <h2>API Health</h2>
          <div className="health-grid">
            <Card className="health-card">
              <div className="health-label">Health Endpoint</div>
              <div className={`health-status-badge ${healthStatus === 'healthy' ? 'healthy' : 'unhealthy'}`}>
                {healthStatus ?? '--'}
              </div>
            </Card>

            <Card className="health-card">
              <div className="health-label">Response Time</div>
              <div className="health-value">
                {responseTime != null ? `${responseTime} ms` : '--'}
              </div>
            </Card>
          </div>
        </div>
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.SettingsTab = SettingsTab;
})();