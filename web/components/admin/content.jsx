(() => {
  const { Card } = window.CFC.Primitives;
  const { useState, useEffect } = React;

  function ContentTab() {
    const [stats, setStats] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    useEffect(() => {
      const fetchStats = async () => {
        try {
          const res = await fetch('/api/visibility/vector-store');
          if (!res.ok) {
            const data = await res.json().catch(() => ({}));
            throw new Error(data.detail || `Failed to fetch stats (${res.status})`);
          }
          const data = await res.json();
          if (data.success) {
            setStats(data);
          } else {
            throw new Error('Vector store returned unsuccessful response');
          }
        } catch (e) {
          setError(e.message);
        } finally {
          setLoading(false);
        }
      };
      fetchStats();
    }, []);

    if (loading) {
      return (
        <div className="tab-content">
          <div className="tab-header">
            <div>
              <h2>Content Library</h2>
              <p className="muted">Manage uploaded documents and files</p>
            </div>
          </div>
          <div style={{ textAlign: 'center', padding: '40px', color: 'var(--color-text-muted)' }}>
            Loading vector store stats...
          </div>
        </div>
      );
    }

    if (error) {
      return (
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
              <p className="empty-state-text">Could not load vector store stats</p>
              <p className="muted" style={{ fontSize: '0.85rem' }}>{error}</p>
            </div>
          </Card>
        </div>
      );
    }

    return (
      <div className="tab-content">
        <div className="tab-header">
          <div>
            <h2>Content Library</h2>
            <p className="muted">Vector store overview for index: <strong>{stats.index_name}</strong></p>
          </div>
        </div>

        <div className="ingestion-stats">
          <Card className="stat-card">
            <div className="stat-label">Total Vectors</div>
            <div className="stat-number">{(stats.total_vectors || 0).toLocaleString()}</div>
          </Card>

          <Card className="stat-card">
            <div className="stat-label">Dimensions</div>
            <div className="stat-number">{stats.dimension || '--'}</div>
          </Card>

          <Card className="stat-card">
            <div className="stat-label">Index Fullness</div>
            <div className="stat-number">{stats.index_fullness != null ? `${(stats.index_fullness * 100).toFixed(1)}%` : '--'}</div>
          </Card>
        </div>

        {stats.namespaces && stats.namespaces.length > 0 && (
          <div style={{ marginTop: '20px' }}>
            <h3 style={{ marginBottom: '12px' }}>Namespaces</h3>
            <div className="user-list">
              {stats.namespaces.map((ns, i) => (
                <Card key={i} className="user-card" style={{ padding: '12px 16px' }}>
                  <div style={{ flex: 1 }}>
                    <strong>{ns.name || '(default)'}</strong>
                  </div>
                  <div style={{ color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                    {(ns.vector_count || 0).toLocaleString()} vectors
                  </div>
                </Card>
              ))}
            </div>
          </div>
        )}

        {(!stats.namespaces || stats.namespaces.length === 0) && stats.total_vectors === 0 && (
          <Card className="content-empty-state" style={{ marginTop: '20px' }}>
            <div className="empty-state-content">
              <div className="empty-state-icon">
                <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
              <p className="empty-state-text">No content uploaded yet</p>
              <p className="muted" style={{ fontSize: '0.85rem' }}>Upload documents in the Upload tab to populate the vector store</p>
            </div>
          </Card>
        )}
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.ContentTab = ContentTab;
})();
