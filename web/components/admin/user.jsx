(() => {
  const { Card, PrimaryButton } = window.CFC.Primitives;
  const { Toggle } = window.CFC.Admin;

  function UsersTab() {
    const [johnSuspended, setJohnSuspended] = React.useState(false);
    const [sarahSuspended, setSarahSuspended] = React.useState(true);

    return (
      <div className="tab-content">
        <div className="tab-header">
          <div>
            <h2>User Management</h2>
            <p className="muted">Invite and manage user access</p>
          </div>
          <PrimaryButton type="button" onClick={() => console.log('Invite user clicked')}>
            <span style={{ marginRight: '0.5rem' }}>👤+</span>
            Invite User
          </PrimaryButton>
        </div>

        <div className="user-list">
          <Card className="user-card">
            <div className="user-avatar">J</div>
            <div className="user-info">
              <div className="user-name-row">
                <h3 className="user-name">John Admin</h3>
                <span className="status-badge status-active">active</span>
                <span className="role-badge">admin</span>
              </div>
              <div className="user-email">admin@cfctech.com</div>
              <div className="user-last-active">Last active: 2/9/2026, 11:01:45 PM</div>
            </div>
            <div className="user-actions">
              <Toggle checked={johnSuspended} onChange={(val) => { console.log('Toggle John:', val); setJohnSuspended(val); }} />
              <button className="btn-delete" onClick={() => console.log('Delete John')}>
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2M10 11v6M14 11v6" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </button>
            </div>
          </Card>

          <Card className="user-card">
            <div className="user-avatar">S</div>
            <div className="user-info">
              <div className="user-name-row">
                <h3 className="user-name">Sarah Developer</h3>
                <span className="status-badge status-suspended">suspended</span>
                <span className="role-badge">developer</span>
              </div>
              <div className="user-email">dev@cfctech.com</div>
              <div className="user-last-active">Last active: 2/9/2026, 10:01:45 PM</div>
            </div>
            <div className="user-actions">
              <Toggle checked={sarahSuspended} onChange={(val) => { console.log('Toggle Sarah:', val); setSarahSuspended(val); }} />
              <button className="btn-delete" onClick={() => console.log('Delete Sarah')}>
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2M10 11v6M14 11v6" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </button>
            </div>
          </Card>

          <Card className="user-card">
            <div className="user-avatar">M</div>
            <div className="user-info">
              <div className="user-name-row">
                <h3 className="user-name">Mike User</h3>
                <span className="status-badge status-pending">pending</span>
                <span className="role-badge">user</span>
              </div>
              <div className="user-email">mike@example.com</div>
              <div className="user-last-active">Last active: 2/8/2026, 11:01:45 PM</div>
            </div>
            <div className="user-actions">
              <button className="btn-resend" onClick={() => console.log('Resend invite to Mike')}>
                Resend
              </button>
              <button className="btn-delete" onClick={() => console.log('Delete Mike')}>
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2M10 11v6M14 11v6" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </button>
            </div>
          </Card>
        </div>
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.UsersTab = UsersTab;
})();