(() => {
  const { Card, PrimaryButton } = window.CFC.Primitives;

  const VIDEO_EXTENSIONS = ['.mp4', '.mov', '.m4v', '.mkv', '.webm'];
  const NON_PDF_UPLOAD_ACCEPT = '.doc,.docx,.txt,.md,.mp4,.mov,.m4v,.mkv,.webm';
  const EMAIL_PDF_ACCEPT = '.pdf,application/pdf';

  function isVideoFile(file) {
    if (!file) return false;
    if (file.type && file.type.startsWith('video/')) return true;
    const lower = (file.name || '').toLowerCase();
    return VIDEO_EXTENSIONS.some((ext) => lower.endsWith(ext));
  }

  function isPdfFile(file) {
    if (!file) return false;
    const lower = (file.name || '').toLowerCase();
    if (lower.endsWith('.pdf')) return true;
    return file.type === 'application/pdf';
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
      xhr.open('POST', '/api/files/upload');
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

    const [emailFile, setEmailFile] = React.useState(null);
    const [emailStatus, setEmailStatus] = React.useState(null);
    const [emailProgress, setEmailProgress] = React.useState(0);

    const [bulkFiles, setBulkFiles] = React.useState([]);
    const [bulkItems, setBulkItems] = React.useState([]);
    const [bulkBusy, setBulkBusy] = React.useState(false);
    const [activeHint, setActiveHint] = React.useState(null);
    // FIX 1: Dedicated bulk error state so PDF errors show on the Bulk Upload
    // card instead of accidentally updating singleStatus.
    const [bulkStatus, setBulkStatus] = React.useState(null);

    const handleSingleChange = (e) => {
      const file = e.target.files?.[0] || null;
      if (file && isPdfFile(file)) {
        setSingleFile(null);
        setSingleStatus({
          state: 'error',
          message: 'PDF uploads are only supported in the Email Upload dropbox.',
        });
        setSingleProgress(0);
        e.target.value = '';
        return;
      }
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
      const hasPdf = files.some(isPdfFile);
      if (hasPdf) {
        setBulkFiles([]);
        setBulkItems([]);
        // FIX 1: Use bulkStatus (not singleStatus) so the error appears
        // beneath the Bulk Upload card where the user is working.
        setBulkStatus({
          state: 'error',
          message: 'PDF files are only supported in the Email Upload dropbox.',
        });
        e.target.value = '';
        return;
      }
      setBulkStatus(null);
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
      setBulkStatus(null);
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

    const handleEmailChange = (e) => {
      const file = e.target.files?.[0] || null;
      if (file && !isPdfFile(file)) {
        setEmailFile(null);
        setEmailStatus({
          state: 'error',
          message: 'Email Upload only accepts PDF files.',
        });
        setEmailProgress(0);
        e.target.value = '';
        return;
      }
      setEmailFile(file);
      setEmailStatus(null);
      setEmailProgress(0);
    };

    const handleEmailUpload = async () => {
      if (!emailFile) return;
      setEmailStatus({ state: 'uploading', message: 'Uploading email PDF…' });
      try {
        const data = await uploadSingleFile(emailFile, setEmailProgress);
        setEmailStatus({
          state: 'done',
          message: 'Email PDF uploaded and ingested.',
          data,
        });
      } catch (err) {
        setEmailStatus({ state: 'error', message: err.message || String(err) });
      }
    };

    const singleDisabled = !singleFile || singleStatus?.state === 'uploading';
    const singleDisabledHint = !singleFile
      ? 'Choose a file first.'
      : 'Upload in progress...';
    const bulkDisabled = bulkBusy || !bulkFiles.length;
    const bulkDisabledHint = !bulkFiles.length
      ? 'Choose files first.'
      : 'Bulk upload in progress...';
    const emailDisabled = !emailFile;
    const emailDisabledHint = 'Choose a PDF file first.';

    return (
      <div className="admin-grid">
        <Card className="admin-card">
          <h2>Upload</h2>
          <p className="muted">Upload a single document or video (PDF excluded). We&apos;ll detect the file type and ingest it appropriately.</p>
          <div className="file-picker">
            <input type="file" accept={NON_PDF_UPLOAD_ACCEPT} onChange={handleSingleChange} className="file-input" />
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
            <span
              style={{ display: 'inline-block', position: 'relative' }}
              onMouseEnter={() => singleDisabled && setActiveHint('single')}
              onMouseLeave={() => setActiveHint(null)}
              onFocus={() => singleDisabled && setActiveHint('single')}
              onBlur={() => setActiveHint(null)}
            >
              <PrimaryButton
                type="button"
                onClick={handleSingleUpload}
                disabled={singleDisabled}
              >
                Upload &amp; ingest
              </PrimaryButton>
              {singleDisabled && activeHint === 'single' && (
                <span
                  role="tooltip"
                  style={{
                    position: 'absolute',
                    left: '50%',
                    transform: 'translateX(-50%)',
                    bottom: 'calc(100% + 8px)',
                    background: '#ffffff',
                    color: '#111827',
                    border: '1px solid #1f2937',
                    borderRadius: '0',
                    boxShadow: 'none',
                    padding: '4px 8px',
                    fontSize: '0.9rem',
                    whiteSpace: 'nowrap',
                    pointerEvents: 'none',
                    zIndex: 20,
                  }}
                >
                  {singleDisabledHint}
                </span>
              )}
            </span>
          </div>
          {singleStatus && (
            <div className={`status-pill status-${singleStatus.state || 'info'}`}>
              {singleStatus.message}
            </div>
          )}
        </Card>

        <Card className="admin-card">
          <h2>Bulk Upload</h2>
          <p className="muted">Select a collection of documents and videos (PDF excluded). Each file is queued, uploaded, and ingested with progress tracking.</p>
          <div className="file-picker">
            <input type="file" accept={NON_PDF_UPLOAD_ACCEPT} multiple onChange={handleBulkChange} className="file-input" />
          </div>
          <div className="admin-actions">
            <span
              style={{ display: 'inline-block', position: 'relative' }}
              onMouseEnter={() => bulkDisabled && setActiveHint('bulk')}
              onMouseLeave={() => setActiveHint(null)}
              onFocus={() => bulkDisabled && setActiveHint('bulk')}
              onBlur={() => setActiveHint(null)}
            >
              <PrimaryButton type="button" onClick={handleBulkUpload} disabled={bulkDisabled}>
                {bulkBusy ? 'Uploading…' : 'Start bulk upload'}
              </PrimaryButton>
              {bulkDisabled && activeHint === 'bulk' && (
                <span
                  role="tooltip"
                  style={{
                    position: 'absolute',
                    left: '50%',
                    transform: 'translateX(-50%)',
                    bottom: 'calc(100% + 8px)',
                    background: '#ffffff',
                    color: '#111827',
                    border: '1px solid #1f2937',
                    borderRadius: '0',
                    boxShadow: 'none',
                    padding: '4px 8px',
                    fontSize: '0.9rem',
                    whiteSpace: 'nowrap',
                    pointerEvents: 'none',
                    zIndex: 20,
                  }}
                >
                  {bulkDisabledHint}
                </span>
              )}
            </span>
          </div>
          {/* FIX 1: Render bulkStatus error here, scoped to this card */}
          {bulkStatus && (
            <div className={`status-pill status-${bulkStatus.state || 'info'}`}>
              {bulkStatus.message}
            </div>
          )}
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

        <Card className="admin-card">
          <h2>Email Upload</h2>
          <p className="muted">Upload email exports as PDF. This is the only upload dropbox that supports PDF files.</p>
          <div className="file-picker">
            <input type="file" accept={EMAIL_PDF_ACCEPT} onChange={handleEmailChange} className="file-input" />
            {emailFile && (
              <div className="file-chip">
                <span>{emailFile.name}</span>
                <span className="file-chip-kind">Email PDF</span>
              </div>
            )}
          </div>
          {emailProgress > 0 && (
            <div className="progress-bar">
              <div className="progress-fill" style={{ width: `${emailProgress}%` }} />
            </div>
          )}
          <div className="admin-actions">
            <span
              style={{ display: 'inline-block', position: 'relative' }}
              onMouseEnter={() => emailDisabled && setActiveHint('email')}
              onMouseLeave={() => setActiveHint(null)}
              onFocus={() => emailDisabled && setActiveHint('email')}
              onBlur={() => setActiveHint(null)}
            >
              <PrimaryButton type="button" onClick={handleEmailUpload} disabled={emailDisabled}>
                Upload Email PDF
              </PrimaryButton>
              {emailDisabled && activeHint === 'email' && (
                <span
                  role="tooltip"
                  style={{
                    position: 'absolute',
                    left: '50%',
                    transform: 'translateX(-50%)',
                    bottom: 'calc(100% + 8px)',
                    background: '#ffffff',
                    color: '#111827',
                    border: '1px solid #1f2937',
                    borderRadius: '0',
                    boxShadow: 'none',
                    padding: '4px 8px',
                    fontSize: '0.9rem',
                    whiteSpace: 'nowrap',
                    pointerEvents: 'none',
                    zIndex: 20,
                  }}
                >
                  {emailDisabledHint}
                </span>
              )}
            </span>
          </div>
          {emailStatus && (
            <div className={`status-pill status-${emailStatus.state || 'info'}`}>
              {emailStatus.message}
            </div>
          )}
        </Card>
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Admin = window.CFC.Admin || {};
  window.CFC.Admin.UploadTab = UploadTab;
})();