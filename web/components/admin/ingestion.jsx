(() => {
  const { Card } = window.CFC.Primitives;
  const { useUser } = window.CFC.UserContext;
  const { useState, useEffect, useCallback } = React;

  function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  function IngestionTab() {
    const { session } = useUser();
    const [stats, setStats] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    const fetchStats = useCallback(() => {
      setLoading(true);
      setError(null);
      fetch('/api/admin/ingestion/stats', {
        headers: { Authorization: `Bearer ${session?.access_token}` },
      })
        .then(res => {
          if (!res.ok) throw new Error(`HTTP ${res.status}`);
          return res.json();
        })
        .then(data => setStats(data))
        .catch(err => {
          console.error('Failed to load ingestion stats:', err);
          setError(err.message);
        })
        .finally(() => setLoading(false));
    }, [session]);

    useEffect(() => { fetchStats(); }, [fetchStats]);

    return (
      <div className="tab-content">
        <div className="tab-header">
          <div>
            <h2>Ingestion Status</h2>
            <p className="muted">Monitor AI content ingestion progress</p>
          </div>
          <button className="btn btn-secondary" onClick={fetchStats} disabled={loading}>
            {loading ? 'Refreshing...' : 'Refresh'}
          </button>
        </div>

        <div className="ingestion-stats">
          <Card className="stat-card">
            <div className="stat-label">Total Documents</div>
            <div className="stat-number">{stats?.total_documents ?? 0}</div>
          </Card>

          <Card className="stat-card">
            <div className="stat-label">Processing</div>
            <div className="stat-number stat-processing">{stats?.processing ?? 0}</div>
          </Card>

          <Card className="stat-card">
            <div className="stat-label">Completed</div>
            <div className="stat-number stat-completed">{stats?.completed ?? 0}</div>
          </Card>
        </div>

        {error && (
          <Card className="ingestion-empty-state">
            <div className="empty-state-content">
              <p className="empty-state-text">Failed to load stats: {error}</p>
            </div>
          </Card>
        )}

        {!error && stats?.documents?.length > 0 && (
          <Card>
            <h3 style={{ margin: '0 0 12px' }}>Documents</h3>
            <table className="documents-table" style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  <th style={{ textAlign: 'left', padding: '8px', borderBottom: '1px solid var(--border)' }}>Name</th>
                  <th style={{ textAlign: 'right', padding: '8px', borderBottom: '1px solid var(--border)' }}>Size</th>
                  <th style={{ textAlign: 'center', padding: '8px', borderBottom: '1px solid var(--border)' }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {stats.documents.map((doc, i) => (
                  <tr key={i}>
                    <td style={{ padding: '8px', borderBottom: '1px solid var(--border)' }}>{doc.name}</td>
                    <td style={{ textAlign: 'right', padding: '8px', borderBottom: '1px solid var(--border)' }}>{formatSize(doc.size)}</td>
                    <td style={{ textAlign: 'center', padding: '8px', borderBottom: '1px solid var(--border)' }}>
                      <span className={`status-badge ${doc.status === 'ingested' ? 'status-ingested' : 'status-pending'}`}>
                        {doc.status}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}

        {!error && stats?.documents?.length === 0 && (
          <Card className="ingestion-empty-state">
            <div className="empty-state-content">
              <div className="empty-state-icon">
                <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4m0 5c0 2.21-3.582 4-8 4s-8-1.79-8-4" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
              <p className="empty-state-text">No documents found</p>
            </div>
          </Card>
        )}
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.IngestionTab = IngestionTab;
})();