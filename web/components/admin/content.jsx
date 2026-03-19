(() => {
  const { Card } = window.CFC.Primitives;
  const { useUser } = window.CFC.UserContext;
  const { useState, useEffect, useCallback, useRef } = React;

  function basename(source) {
    if (!source) return '—';
    return source.split(/[\\/]/).pop() || source;
  }

  function ContentTab() {
    const { session } = useUser();
    const [documents, setDocuments] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [deleteTarget, setDeleteTarget] = useState(null);
    const [actionState, setActionState] = useState({});
    const fileInputRef = useRef(null);
    const replaceTargetRef = useRef(null);

    const authHeaders = useCallback(() => ({
      Authorization: `Bearer ${session?.access_token}`,
    }), [session]);

    const fetchDocuments = useCallback(() => {
      setLoading(true);
      setError(null);
      fetch('/api/admin/documents', { headers: authHeaders() })
        .then(res => {
          if (!res.ok) throw new Error(`HTTP ${res.status}`);
          return res.json();
        })
        .then(data => setDocuments(data.documents || []))
        .catch(err => setError(err.message))
        .finally(() => setLoading(false));
    }, [session]);

    useEffect(() => { fetchDocuments(); }, [fetchDocuments]);

    const setDocAction = (docId, patch) =>
      setActionState(prev => ({ ...prev, [docId]: { ...(prev[docId] || {}), ...patch } }));

    // --- Delete ---
    const handleDelete = async (docId) => {
      setDeleteTarget(null);
      setDocAction(docId, { busy: true, error: null, message: null });
      try {
        const res = await fetch(`/api/admin/documents/${encodeURIComponent(docId)}`, {
          method: 'DELETE',
          headers: authHeaders(),
        });
        if (!res.ok) {
          const data = await res.json().catch(() => ({}));
          throw new Error(data.detail || `Delete failed (${res.status})`);
        }
        setDocuments(prev => prev.filter(d => d.doc_id !== docId));
        setDocAction(docId, { busy: false });
      } catch (err) {
        setDocAction(docId, { busy: false, error: err.message });
      }
    };

    // --- Download ---
    const handleDownload = async (docId) => {
      try {
        const res = await fetch(`/api/admin/documents/${encodeURIComponent(docId)}/download`, {
          headers: authHeaders(),
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.detail || `Download failed (${res.status})`);
        const a = document.createElement('a');
        a.href = data.url;
        a.download = data.filename || docId;
        a.target = '_blank';
        a.click();
      } catch (err) {
        setDocAction(docId, { error: err.message });
      }
    };

    // --- Replace ---
    const triggerReplace = (docId) => {
      replaceTargetRef.current = docId;
      fileInputRef.current.value = '';
      fileInputRef.current.click();
    };

    const handleReplaceFileChosen = async (e) => {
      const file = e.target.files?.[0];
      const docId = replaceTargetRef.current;
      if (!file || !docId) return;

      setDocAction(docId, { busy: true, error: null, message: null });
      const form = new FormData();
      form.append('file', file);

      try {
        const res = await fetch(`/api/admin/documents/${encodeURIComponent(docId)}/replace`, {
          method: 'PUT',
          headers: authHeaders(),
          body: form,
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.detail || `Replace failed (${res.status})`);
        setDocAction(docId, { busy: false, message: 'Replaced successfully.' });
        fetchDocuments();
      } catch (err) {
        setDocAction(docId, { busy: false, error: err.message });
      }
    };

    return (
      <div className="tab-content">
        {/* Hidden file input for replace */}
        <input
          type="file"
          ref={fileInputRef}
          style={{ display: 'none' }}
          accept=".doc,.docx"
          onChange={handleReplaceFileChosen}
        />

        {/* Delete Confirmation Modal — same pattern as user.jsx */}
        {deleteTarget && (
          <>
            <div
              onClick={() => setDeleteTarget(null)}
              style={{
                position: 'fixed',
                top: 0, left: 0, right: 0, bottom: 0,
                backgroundColor: 'rgba(0, 0, 0, 0.5)',
                zIndex: 9999,
              }}
            />
            <div style={{
              position: 'fixed',
              top: '50%', left: '50%',
              transform: 'translate(-50%, -50%)',
              zIndex: 10000,
              width: '90%',
              maxWidth: '440px',
              padding: '24px',
              borderRadius: '12px',
              backgroundColor: 'var(--color-surface, #1e293b)',
              border: '1px solid var(--color-border, #334155)',
              boxShadow: '0 20px 60px rgba(0, 0, 0, 0.4)',
              color: 'var(--color-text, #e2e8f0)',
            }}>
              <h3 style={{ margin: '0 0 8px', fontSize: '1.1rem' }}>Delete Document</h3>
              <p style={{ margin: '0 0 20px', color: 'var(--color-text-muted, #94a3b8)', fontSize: '0.95rem' }}>
                Are you sure you want to <strong>permanently delete</strong> {deleteTarget}? This will remove it from the knowledge base and cannot be undone.
              </p>
              <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
                <button className="btn-secondary" onClick={() => setDeleteTarget(null)}>Cancel</button>
                <button
                  className="btn-secondary"
                  style={{ backgroundColor: '#dc2626', color: 'white', border: 'none' }}
                  onClick={() => handleDelete(deleteTarget)}
                >
                  Delete Permanently
                </button>
              </div>
            </div>
          </>
        )}

        <div className="tab-header">
          <div>
            <h2>Content Library</h2>
            <p className="muted">Documents currently in the knowledge base</p>
          </div>
          <button className="btn btn-secondary" onClick={fetchDocuments} disabled={loading}>
            {loading ? 'Loading...' : 'Refresh'}
          </button>
        </div>

        {error && (
          <div style={{ padding: '10px 14px', borderRadius: '8px', backgroundColor: '#fef2f2', color: '#dc2626', marginBottom: '16px', fontSize: '0.9rem' }}>
            {error}
          </div>
        )}

        {loading && (
          <div style={{ textAlign: 'center', padding: '40px', color: 'var(--color-text-muted)' }}>
            Loading documents...
          </div>
        )}

        {!loading && !error && documents.length === 0 && (
          <Card className="content-empty-state">
            <div className="empty-state-content">
              <div className="empty-state-icon">
                <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
              </div>
              <p className="empty-state-text">No documents in the knowledge base</p>
              <p className="muted" style={{ fontSize: '0.85rem' }}>Upload documents in the Upload tab to get started</p>
            </div>
          </Card>
        )}

        {!loading && documents.length > 0 && (
          <div className="user-list">
            {documents.map((doc) => {
              const state = actionState[doc.doc_id] || {};
              return (
                <Card key={doc.doc_id} className="user-card">
                  {/* Doc icon */}
                  <div className="user-avatar" style={{ fontSize: '1.2rem', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/>
                      <polyline points="14 2 14 8 20 8"/>
                      <line x1="16" y1="13" x2="8" y2="13"/>
                      <line x1="16" y1="17" x2="8" y2="17"/>
                      <polyline points="10 9 9 9 8 9"/>
                    </svg>
                  </div>

                  <div className="user-info">
                    <div className="user-name-row">
                      <h3 className="user-name">{basename(doc.source)}</h3>
                      <span className="status-badge status-ingested">{doc.source_type}</span>
                    </div>
                    <div className="user-email">{doc.doc_id}</div>
                    <div className="user-last-active">{doc.chunk_count} chunks indexed</div>
                    {(state.error || state.message) && (
                      <div style={{ marginTop: '4px', fontSize: '0.82rem', color: state.error ? '#dc2626' : 'var(--color-success, #16a34a)' }}>
                        {state.error || state.message}
                      </div>
                    )}
                  </div>

                  <div className="user-actions">
                    {state.busy ? (
                      <span style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>Working…</span>
                    ) : (
                      <>
                        {/* Download button */}
                        <button
                          className="btn-delete"
                          title="Download original file"
                          onClick={() => handleDownload(doc.doc_id)}
                          aria-label={`Download ${basename(doc.source)}`}
                        >
                          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/>
                            <polyline points="7 10 12 15 17 10"/>
                            <line x1="12" y1="15" x2="12" y2="3"/>
                          </svg>
                        </button>
                        {/* Replace button — pencil icon, same style as btn-delete */}
                        <button
                          className="btn-delete"
                          title="Replace document"
                          onClick={() => triggerReplace(doc.doc_id)}
                          aria-label={`Replace ${basename(doc.source)}`}
                        >
                          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7"/>
                            <path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z"/>
                          </svg>
                        </button>
                        {/* Delete button — exact same as user.jsx */}
                        <button
                          className="btn-delete"
                          title="Delete document"
                          onClick={() => setDeleteTarget(doc.doc_id)}
                          aria-label={`Delete ${basename(doc.source)}`}
                        >
                          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2M10 11v6M14 11v6" strokeLinecap="round" strokeLinejoin="round"/>
                          </svg>
                        </button>
                      </>
                    )}
                  </div>
                </Card>
              );
            })}
          </div>
        )}
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.ContentTab = ContentTab;
})();
