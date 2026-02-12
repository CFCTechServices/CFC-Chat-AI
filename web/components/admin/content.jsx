(() => {
  const { Card } = window.CFC.Primitives;

  function ContentTab() {
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
            <p className="empty-state-text">No content uploaded yet</p>
          </div>
        </Card>
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.ContentTab = ContentTab;
})();