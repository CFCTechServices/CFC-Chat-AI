(() => {
  const { Layout } = window.CFC.Layout;
  const { Card } = window.CFC.Primitives;

  const AdminNS = window.CFC.Admin || {};
  const TabButton = AdminNS.TabButton || function TabButtonFallback({ active, onClick, label }) {
    return (
      <button className={`tab-button ${active ? 'active' : ''}`} onClick={onClick} type="button">
        <span>{label}</span>
      </button>
    );
  };

  const UploadTab = AdminNS.UploadTab || function UploadFallback() {
    return (
      <div className="tab-content">
        <Card className="admin-card">
          <h2>Upload</h2>
          <p className="muted">Upload tab component not found. Ensure /ui/components/admin/upload.jsx is loaded.</p>
        </Card>
      </div>
    );
  };

  const UsersTab = AdminNS.UsersTab || function UsersFallback() {
    return (
      <div className="tab-content">
        <Card className="admin-card">
          <h2>Users</h2>
          <p className="muted">Users tab component not found. Ensure /ui/components/admin/users.jsx is loaded.</p>
        </Card>
      </div>
    );
  };

  const ContentTab = AdminNS.ContentTab || function ContentFallback() {
    return (
      <div className="tab-content">
        <Card className="admin-card">
          <h2>Content Library</h2>
          <p className="muted">Content tab component not found. Ensure /ui/components/admin/content.jsx is loaded.</p>
        </Card>
      </div>
    );
  };

  const IngestionTab = AdminNS.IngestionTab || function IngestionFallback() {
    return (
      <div className="tab-content">
        <Card className="admin-card">
          <h2>Ingestion</h2>
          <p className="muted">Ingestion tab component not found. Ensure /ui/components/admin/ingestion.jsx is loaded.</p>
        </Card>
      </div>
    );
  };

  const SettingsTab = AdminNS.SettingsTab || function SettingsFallback() {
    return (
      <div className="tab-content">
        <Card className="admin-card">
          <h2>Settings</h2>
          <p className="muted">Settings tab component not found. Ensure /ui/components/admin/settings.jsx is loaded.</p>
        </Card>
      </div>
    );
  };

  function AdminPage() {
    const [activeTab, setActiveTab] = React.useState('users');

    return (
      <Layout>
        <div className="page admin-page">
          <div className="page-header-row">
            <div>
              <h1>Admin Console</h1>
              <p>Manage users, uploads, and system settings</p>
            </div>
          </div>

          <div className="admin-tabs">
            <TabButton active={activeTab === 'users'} onClick={() => setActiveTab('users')} icon="👥" label="Users" />
            <TabButton active={activeTab === 'upload'} onClick={() => setActiveTab('upload')} icon="📤" label="Upload" />
            <TabButton active={activeTab === 'content'} onClick={() => setActiveTab('content')} icon="📄" label="Content" />
            <TabButton active={activeTab === 'ingestion'} onClick={() => setActiveTab('ingestion')} icon="🗄️" label="Ingestion" />
            <TabButton active={activeTab === 'settings'} onClick={() => setActiveTab('settings')} icon="⚙️" label="Settings" />
          </div>

          {activeTab === 'upload' && <UploadTab />}
          {activeTab === 'users' && <UsersTab />}
          {activeTab === 'content' && <ContentTab />}
          {activeTab === 'ingestion' && <IngestionTab />}
          {activeTab === 'settings' && <SettingsTab />}
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.AdminPage = AdminPage;
})();
