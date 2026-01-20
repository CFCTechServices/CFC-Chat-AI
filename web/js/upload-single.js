// Single file upload with drag & drop
import { createItem, setProgress, setStatus, setDetail } from './helpers.js';

export function initSingleUpload({ dropzoneId, fileInputId, uploadsId }) {
  const dz = document.getElementById(dropzoneId);
  const fileInput = document.getElementById(fileInputId);
  const uploads = document.getElementById(uploadsId);

  if (!dz || !fileInput || !uploads) return;

  const uploadFile = (file) => {
    const item = createItem(file.name);
    uploads.prepend(item);

    return new Promise((resolve) => {
      const form = new FormData();
      form.append('file', file);

      const xhr = new XMLHttpRequest();
      xhr.open('POST', '/files/upload');
      xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
          const pct = Math.round((e.loaded / e.total) * 100);
          setProgress(item, pct);
          setStatus(item, `Uploading… ${pct}%`);
        }
      });
      xhr.onreadystatechange = () => {
        if (xhr.readyState === 4) {
          try {
            const data = JSON.parse(xhr.responseText || '{}');
            if (xhr.status >= 200 && xhr.status < 300) {
              setProgress(item, 100);
              setStatus(item, 'Ingested', true);
              const info = data?.ingestion || {};
              setDetail(item, `sections: ${info.sections_processed ?? '-'}, chunks: ${info.chunks_processed ?? '-'}`);
              resolve(data);
            } else {
              setStatus(item, 'Failed');
              setDetail(item, data?.error || data?.ingestion?.error || 'Upload failed', true);
              resolve(null);
            }
          } catch (err) {
            setStatus(item, 'Failed');
            setDetail(item, 'Unexpected response', true);
            resolve(null);
          }
        }
      };
      xhr.send(form);
    });
  };

  dz.addEventListener('click', () => fileInput.click());
  dz.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') fileInput.click();
  });
  dz.addEventListener('dragover', (e) => {
    e.preventDefault();
    dz.classList.add('dragover');
  });
  dz.addEventListener('dragleave', () => dz.classList.remove('dragover'));
  dz.addEventListener('drop', (e) => {
    e.preventDefault();
    dz.classList.remove('dragover');
    const files = Array.from(e.dataTransfer.files || []);
    if (files.length) uploadFile(files[0]);
  });
  fileInput.addEventListener('change', (e) => {
    const file = e.target.files?.[0];
    if (file) uploadFile(file);
  });
}
