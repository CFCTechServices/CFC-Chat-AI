(() => {
  const { Card, PrimaryButton } = window.CFC.Primitives;

  const VIDEO_EXTENSIONS = ['.mp4', '.mov', '.m4v', '.mkv', '.webm'];

  function isVideoFile(file) {
    if (!file) return false;
    if (file.type && file.type.startsWith('video/')) return true;
    const lower = (file.name || '').toLowerCase();
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

  function UploadTab() {
    const [singleFile, setSingleFile] = React.useState(null);
    const [singleStatus, setSingleStatus] = React.useState(null);
    const [singleProgress, setSingleProgress] = React.useState(0);

    const [bulkFiles, setBulkFiles] = React.useState([]);
    const [bulkItems, setBulkItems] = React.useState([]);
    const [bulkBusy, setBulkBusy] = React.useState(false);

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
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.UploadTab = UploadTab;
})();