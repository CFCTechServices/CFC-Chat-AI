(() => {
  const { Layout } = window.CFC.Layout;
  const { Card, PrimaryButton } = window.CFC.Primitives;

  const VIDEO_EXTENSIONS = ['.mp4', '.mov', '.m4v', '.mkv', '.webm'];

  // Tab button component
  function TabButton({ active, onClick, icon, label }) {
    return (
      <button
        className={`tab-button ${active ? 'active' : ''}`}
        onClick={onClick}
        type="button"
      >
        {icon && <span className="tab-icon">{icon}</span>}
        <span>{label}</span>
      </button>
    );
  }

  // Toggle Component
  function Toggle({ checked, onChange }) {
    return (
      <button
        className={`toggle ${checked ? 'checked' : ''}`}
        onClick={() => onChange(!checked)}
        type="button"
        role="switch"
        aria-checked={checked}
      >
        <span className="toggle-track">
          <span className="toggle-thumb" />
        </span>
      </button>
    );
  }

  function isVideoFile(file) {
    if (!file) return false;
    if (file.type && file.type.startsWith('video/')) return true;
    const name = file.name || '';
    const lower = name.toLowerCase();
    return VIDEO_EXTENSIONS.some((ext) => lower.endsWith(ext));
  }

  function fileSlug(file) {
    const name = (file?.name || '').toLowerCase();
    return name.replace(/\.[^.]+$/, '').replace(/\s+/g, '-');
  }

  async function uploadSingleFile(file, onProgress) {
    if (!file) return { error: 'No file selected' };

    if (isVideoFile(file)) {
      const form = new FormData();
      form.append('slug', fileSlug(file));
      form.append('file', file);
      form.append('model', 'small');

      const res = await fetch('/api/videos/upload', { method: 'POST', body: form });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        throw new Error(data.detail || 'Video upload failed');
      }
      if (onProgress) onProgress(100);
      return res.json();
    }

    return new Promise((resolve, reject) => {
      const form = new FormData();
      form.append('file', file);
      const xhr = new XMLHttpRequest();
      xhr.open('POST', '/files/upload');
      xhr.upload.addEventListener('progress', (e) => {
        if (onProgress && e.lengthComputable) {
          const pct = Math.round((e.loaded / e.total) * 100);
          onProgress(pct);
        }
      });
      xhr.onreadystatechange = () => {
        if (xhr.readyState === 4) {
          try {
            const data = JSON.parse(xhr.responseText || '{}');
            if (xhr.status >= 200 && xhr.status < 300) {
              if (onProgress) onProgress(100);
              resolve(data);
            } else {
              reject(new Error(data.error || data.detail || 'Upload failed'));
            }
          } catch (err) {
            reject(err);
          }
        }
      };
      xhr.send(form);
    });
  }

  function AdminPage() {
    const [activeTab, setActiveTab] = React.useState('users');

    const [singleFile, setSingleFile] = React.useState(null);
    const [singleStatus, setSingleStatus] = React.useState(null);
    const [singleProgress, setSingleProgress] = React.useState(0);

    const [bulkFiles, setBulkFiles] = React.useState([]);
    const [bulkItems, setBulkItems] = React.useState([]);
    const [bulkBusy, setBulkBusy] = React.useState(false);

    const [autoApprove, setAutoApprove] = React.useState(false);
    const [emailNotifications, setEmailNotifications] = React.useState(true);
    const [maintenanceMode, setMaintenanceMode] = React.useState(false);

    const handleSingleChange = (e) => {
      const file = e.target.files?.[0] || null;
      setSingleFile(file);
      setSingleStatus(null);
      setSingleProgress(0);
    };

    const handleSingleUpload = async () => {
      if (!singleFile) return;
      setSingleStatus({ state: 'uploading', message: 'Uploading…' });
      try {
        const data = await uploadSingleFile(singleFile, setSingleProgress);
        const isVideo = isVideoFile(singleFile);
        setSingleStatus({
          state: 'done',
          message: isVideo ? 'Video uploaded and transcribed.' : 'Document uploaded and ingested.',
          data,
        });
      } catch (err) {
        setSingleStatus({ state: 'error', message: err.message || String(err) });
      }
    };

    const handleBulkChange = (e) => {
      const files = Array.from(e.target.files || []);
      setBulkFiles(files);
      setBulkItems(
        files.map((f) => ({
          id: `${f.name}-${Math.random().toString(36).slice(2)}`,
          file: f,
          progress: 0,
          status: 'ready',
          detail: '',
        })),
      );
    };

    const handleBulkUpload = async () => {
      if (!bulkFiles.length) return;
      setBulkBusy(true);
      const updated = [...bulkItems];

      const updateItem = (idx, patch) => {
        updated[idx] = { ...updated[idx], ...patch };
        setBulkItems([...updated]);
      };

      for (let i = 0; i < bulkFiles.length; i += 1) {
        const file = bulkFiles[i];
        updateItem(i, { status: 'uploading', detail: '' });
        try {
          const data = await uploadSingleFile(file, (pct) => updateItem(i, { progress: pct }));
          const isVideo = isVideoFile(file);
          updateItem(i, {
            status: 'done',
            progress: 100,
            detail: isVideo ? 'Video uploaded and transcribed.' : 'Document uploaded and ingested.',
            data,
          });
        } catch (err) {
          updateItem(i, { status: 'error', detail: err.message || String(err) });
        }
      }

      setBulkBusy(false);
    };

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
            <TabButton
              active={activeTab === 'users'}
              onClick={() => setActiveTab('users')}
              icon="👥"
              label="Users"
            />
            <TabButton
              active={activeTab === 'upload'}
              onClick={() => setActiveTab('upload')}
              icon="📤"
              label="Upload"
            />
            <TabButton
              active={activeTab === 'content'}
              onClick={() => setActiveTab('content')}
              icon="📄"
              label="Content"
            />
            <TabButton
              active={activeTab === 'ingestion'}
              onClick={() => setActiveTab('ingestion')}
              icon="🗄️"
              label="Ingestion"
            />
            <TabButton
              active={activeTab === 'settings'}
              onClick={() => setActiveTab('settings')}
              icon="⚙️"
              label="Settings"
            />
          </div>

          {activeTab === 'upload' && (
            <div className="admin-grid">
              <Card className="admin-card">
                <h2>Upload</h2>
                <p className="muted">Upload a single document or video. We&apos;ll detect the file type and ingest it appropriately.</p>
                <div className="file-picker">
                  <input type="file" onChange={handleSingleChange} className="file-input" />
                  {singleFile && (
                    <div className="file-chip">
                      <span>{singleFile.name}</span>
                      <span className="file-chip-kind">{isVideoFile(singleFile) ? 'Video' : 'Document'}</span>
                    </div>
                  )}
                </div>
                {singleProgress > 0 && (
                  <div className="progress-bar">
                    <div className="progress-fill" style={{ width: `${singleProgress}%` }} />
                  </div>
                )}
                <div className="admin-actions">
                  <PrimaryButton type="button" onClick={handleSingleUpload}>Upload &amp; ingest</PrimaryButton>
                </div>
                {singleStatus && (
                  <div className={`status-pill status-${singleStatus.state || 'info'}`}>
                    {singleStatus.message}
                  </div>
                )}
              </Card>

              <Card className="admin-card">
                <h2>Bulk Upload</h2>
                <p className="muted">Select a collection of documents and videos. Each file is queued, uploaded, and ingested with progress tracking.</p>
                <div className="file-picker">
                  <input type="file" multiple onChange={handleBulkChange} className="file-input" />
                </div>
                <div className="admin-actions">
                  <PrimaryButton type="button" onClick={handleBulkUpload} disabled={bulkBusy || !bulkFiles.length}>
                    {bulkBusy ? 'Uploading…' : 'Start bulk upload'}
                  </PrimaryButton>
                </div>
                <div className="bulk-list">
                  {bulkItems.map((item) => (
                    <div key={item.id} className="bulk-item">
                      <div className="bulk-row">
                        <span className="bulk-name">{item.file.name}</span>
                        <span className={`bulk-status bulk-${item.status}`}>
                          {item.status === 'ready' && 'Ready'}
                          {item.status === 'uploading' && 'Uploading…'}
                          {item.status === 'done' && 'Ingested'}
                          {item.status === 'error' && 'Error'}
                        </span>
                      </div>
                      <div className="progress-bar small">
                        <div className="progress-fill" style={{ width: `${item.progress || 0}%` }} />
                      </div>
                      {item.detail && <div className="bulk-detail">{item.detail}</div>}
                    </div>
                  ))}
                </div>
              </Card>
            </div>
          )}

          {activeTab === 'users' && (
            <div className="tab-content">
              <div className="tab-header">
                <div>
                  <h2>User Management</h2>
                  <p className="muted">Invite and manage user access</p>
                </div>
                <PrimaryButton 
                  type="button" 
                  onClick={() => console.log('Invite user clicked')}
                >
                  <span style={{ marginRight: '0.5rem' }}>👤+</span>
                  Invite User
                </PrimaryButton>
              </div>
              <Card>
                <h2>Users</h2>
                <p>User management coming soon...</p>
              </Card>
            </div>
          )}

          {activeTab === 'content' && (
            <div className="tab-content">
              <div className="tab-header">
                <div>
                  <h2>Content Library</h2>
                  <p className="muted">Manage uploaded documents and files</p>
                </div>
              </div>
              <Card className="content-empty-state">
                <div className="empty-state-content">
                  <div className="empty-state-icon">
                    <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                      <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" strokeLinecap="round" strokeLinejoin="round"/>
                    </svg>
                  </div>
                  <p className="empty-state-text">No content uploaded yet</p>
                </div>
              </Card>
            </div>
          )}

          {activeTab === 'ingestion' && (
            <div className="tab-content">
              <div className="tab-header">
                <div>
                  <h2>Ingestion Status</h2>
                  <p className="muted">Monitor AI content ingestion progress</p>
                </div>
              </div>
              
              <div className="ingestion-stats">
                <Card className="stat-card">
                  <div className="stat-label">Total Documents</div>
                  <div className="stat-number">0</div>
                </Card>
                
                <Card className="stat-card">
                  <div className="stat-label">Processing</div>
                  <div className="stat-number stat-processing">0</div>
                </Card>
                
                <Card className="stat-card">
                  <div className="stat-label">Completed</div>
                  <div className="stat-number stat-completed">0</div>
                </Card>
              </div>
              
              <Card className="ingestion-empty-state">
                <div className="empty-state-content">
                  <div className="empty-state-icon">
                    <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                      <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4m0 5c0 2.21-3.582 4-8 4s-8-1.79-8-4" strokeLinecap="round" strokeLinejoin="round"/>
                    </svg>
                  </div>
                  <p className="empty-state-text">No ingestion jobs running</p>
                </div>
              </Card>
            </div>
          )}

          {activeTab === 'settings' && (
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
                      checked={autoApprove} 
                      onChange={setAutoApprove}
                    />
                  </div>

                  <div className="setting-item">
                    <div className="setting-info">
                      <h3 className="setting-title">Email notifications</h3>
                      <p className="setting-description">Send alerts for ingestion completion</p>
                    </div>
                    <Toggle 
                      checked={emailNotifications} 
                      onChange={setEmailNotifications}
                    />
                  </div>

                  <div className="setting-item">
                    <div className="setting-info">
                      <h3 className="setting-title">Maintenance mode</h3>
                      <p className="setting-description">Restrict access for system maintenance</p>
                    </div>
                    <Toggle 
                      checked={maintenanceMode} 
                      onChange={setMaintenanceMode}
                    />
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
          )}
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.AdminPage = AdminPage;
})();