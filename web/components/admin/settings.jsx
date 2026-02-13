(() => {
  const { Card } = window.CFC.Primitives;
  const { Toggle } = window.CFC.Admin;

  function SettingsTab() {
    const [autoApprove, setAutoApprove] = React.useState(false);
    const [emailNotifications, setEmailNotifications] = React.useState(true);
    const [maintenanceMode, setMaintenanceMode] = React.useState(false);

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
              <Toggle checked={autoApprove} onChange={setAutoApprove} />
            </div>

            <div className="setting-item">
              <div className="setting-info">
                <h3 className="setting-title">Email notifications</h3>
                <p className="setting-description">Send alerts for ingestion completion</p>
              </div>
              <Toggle checked={emailNotifications} onChange={setEmailNotifications} />
            </div>

            <div className="setting-item">
              <div className="setting-info">
                <h3 className="setting-title">Maintenance mode</h3>
                <p className="setting-description">Restrict access for system maintenance</p>
              </div>
              <Toggle checked={maintenanceMode} onChange={setMaintenanceMode} />
            </div>
          </div>
        </Card>

        <div className="api-health-section">
          <h2>API Health</h2>
          <div className="health-grid">
            <Card className="health-card">
              <div className="health-label">Health Endpoint</div>
              <div className="health-status-badge">--</div>
            </Card>

            <Card className="health-card">
              <div className="health-label">Response Time</div>
              <div className="health-value">--</div>
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