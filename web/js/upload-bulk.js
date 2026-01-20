// Bulk file upload with concurrency control
import { createItem, setProgress, setStatus, setDetail } from './helpers.js';

const pLimit = (n) => {
  const queue = [];
  let activeCount = 0;
  const next = () => {
    activeCount--;
    if (queue.length) queue.shift()();
  };
  const run = async (fn, resolve) => {
    activeCount++;
    const result = (async () => fn())();
    resolve(result);
    try {
      await result;
    } finally {
      next();
    }
  };
  const enqueue = (fn) =>
    new Promise((resolve) => {
      const task = () => run(fn, resolve);
      activeCount < n ? task() : queue.push(task);
    });
  return (fn) => enqueue(fn);
};

export function initBulkUpload({ bulkInputId, bulkStartId, bulkUploadsId }) {
  const bulkInput = document.getElementById(bulkInputId);
  const bulkStart = document.getElementById(bulkStartId);
  const bulkUploads = document.getElementById(bulkUploadsId);

  if (!bulkInput || !bulkStart || !bulkUploads) return;

  const limit = pLimit(3);

  const bulkUpload = async (files) => {
    if (!files.length) return;
    bulkStart.disabled = true;
    const items = files.map((f) => {
      const el = createItem(f.name);
      bulkUploads.prepend(el);
      return { file: f, el };
    });

    await Promise.all(
      items.map(({ file, el }) =>
        limit(() =>
          new Promise((resolve) => {
            const form = new FormData();
            form.append('file', file);
            const xhr = new XMLHttpRequest();
            xhr.open('POST', '/files/upload');
            xhr.upload.addEventListener('progress', (e) => {
              if (e.lengthComputable) setProgress(el, Math.round((e.loaded / e.total) * 100));
            });
            xhr.onreadystatechange = () => {
              if (xhr.readyState === 4) {
                try {
                  const data = JSON.parse(xhr.responseText || '{}');
                  if (xhr.status >= 200 && xhr.status < 300) {
                    setProgress(el, 100);
                    setStatus(el, 'Ingested', true);
                    const info = data?.ingestion || {};
                    setDetail(el, `sections: ${info.sections_processed ?? '-'}, chunks: ${info.chunks_processed ?? '-'}`);
                  } else {
                    setStatus(el, 'Failed');
                    setDetail(el, data?.error || data?.ingestion?.error || 'Upload failed', true);
                  }
                } catch {
                  setStatus(el, 'Failed');
                  setDetail(el, 'Unexpected response', true);
                }
                resolve();
              }
            };
            xhr.send(form);
          }),
        ),
      ),
    );
    bulkStart.disabled = false;
  };

  bulkStart.addEventListener('click', () => {
    const files = Array.from(bulkInput.files || []);
    bulkUpload(files);
  });
}
