(() => {
  const { Card } = window.CFC.Primitives;

  function IngestionTab() {
    return (
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
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.IngestionTab = IngestionTab;
})();