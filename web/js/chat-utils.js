// Shared chat utilities – helpers and hooks used across chat components
(() => {
  function formatTimecode(seconds) {
    if (seconds == null || Number.isNaN(seconds)) return null;
    const total = Math.max(0, Math.floor(seconds));
    const hrs = Math.floor(total / 3600);
    const mins = Math.floor((total % 3600) / 60);
    const secs = total % 60;
    const base = [hrs, mins, secs]
      .filter((v, idx) => v > 0 || idx > 0)
      .map((v) => String(v).padStart(2, '0'));
    return base.join(':');
  }

  function useModal() {
    const [content, setContent] = React.useState(null);
    const open = (c) => setContent(c);
    const close = () => setContent(null);

    const modal = content ? (
      <div className="modal-backdrop" onClick={close}>
        <div className="modal-body" onClick={(e) => e.stopPropagation()}>
          <button className="modal-close" type="button" onClick={close}>
            ×
          </button>
          {content}
        </div>
      </div>
    ) : null;

    return { open, close, modal };
  }

  function buildVideoSegmentsFromAnswer(data) {
    const url = data.answer_video_url || (Array.isArray(data.video_context) && data.video_context[0]?.video_url);
    if (!url) return [];

    const timestamps = [];
    const start = data.answer_start_seconds ?? data.video_context?.[0]?.start_seconds;
    const end = data.answer_end_seconds ?? data.video_context?.[0]?.end_seconds;

    if (start != null && end != null && end > start) {
      const span = end - start;
      const step = span / 3;
      const points = [start, start + step, start + 2 * step, end];
      points.slice(0, 4).forEach((sec) => {
        const label = formatTimecode(sec);
        if (label) timestamps.push({ seconds: sec, label });
      });
    } else if (Array.isArray(data.video_context)) {
      data.video_context.slice(0, 4).forEach((clip) => {
        const sec = clip.start_seconds ?? 0;
        const label = clip.timestamp || formatTimecode(sec) || 'Clip';
        timestamps.push({ seconds: sec, label });
      });
    }

    return [{ type: 'video', url, timestamps }];
  }

  function buildImageSegmentsFromAnswer(data) {
    if (data.relevant_images && Array.isArray(data.relevant_images) && data.relevant_images.length > 0) {
      return data.relevant_images.map((img) => {
        const path = img.path || '';
        const url = path.startsWith('http://') || path.startsWith('https://') ? path : `/content/images/${path}`;
        return { type: 'image', url, alt: img.alt_text || 'Document image', position: img.position, path };
      });
    }
    return [];
  }

  window.CFC = window.CFC || {};
  window.CFC.ChatUtils = {
    formatTimecode,
    useModal,
    buildVideoSegmentsFromAnswer,
    buildImageSegmentsFromAnswer,
  };
})();
