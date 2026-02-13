// Chat message bubble, typing indicator, video bubble, and feedback actions
(() => {
  function ChatMessage({ message, onImageClick, onVideoClick }) {
    const isUser = message.role === 'user';

    const escapeHtml = (text) =>
      text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');

    const renderMarkdown = React.useCallback((text) => {
      if (!text) return '';
      const escaped = escapeHtml(text);
      const lines = escaped.split(/\r?\n/);
      const blocks = [];
      let list = [];

      const flushList = () => {
        if (list.length) {
          blocks.push(`<ul>${list.map((item) => `<li>${item}</li>`).join('')}</ul>`);
          list = [];
        }
      };

      const formatInline = (value) =>
        value
          .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
          .replace(/\*(.+?)\*/g, '<em>$1</em>');

      lines.forEach((raw) => {
        const line = raw.trim();
        if (!line) {
          flushList();
          return;
        }
        const bullet = line.match(/^[-*]\s+(.*)/);
        if (bullet) {
          list.push(formatInline(bullet[1]));
          return;
        }
        flushList();
        blocks.push(`<p>${formatInline(line)}</p>`);
      });
      flushList();
      return blocks.join('\n');
    }, []);

    const MarkdownText = ({ text }) => {
      const html = React.useMemo(() => renderMarkdown(text), [text, renderMarkdown]);
      return <div className="chat-text markdown" dangerouslySetInnerHTML={{ __html: html }} />;
    };

    const renderSegments = (segments, fallbackText) => {
      if (segments && segments.length) {
        return segments.map((seg, idx) => {
          if (seg.type === 'text') return <MarkdownText key={idx} text={seg.text} />;
          if (seg.type === 'image') return <img key={idx} src={seg.url} alt={seg.alt || 'Image'} className="chat-image" onClick={() => onImageClick && onImageClick(seg.url)} />;
          if (seg.type === 'video') return <VideoBubble key={idx} segment={seg} onVideoClick={onVideoClick} />;
          return null;
        });
      }
      return <MarkdownText text={fallbackText} />;
    };

    if (message.typing) {
      return (
        <div className="chat-message bot">
          <div className="chat-bubble typing-bubble">
            <div className="typing-dots-container">
              <span className="typing-dot" />
              <span className="typing-dot" />
              <span className="typing-dot" />
            </div>
            {message.showThinking && <span className="typing-message">Assistant is thinking</span>}
          </div>
        </div>
      );
    }

    return (
      <div className={`chat-message ${isUser ? 'user' : 'bot'}`}>
        {isUser ? (
          <div className="chat-bubble">
            {renderSegments(message.segments, message.text)}
          </div>
        ) : (
          <div className="chat-bubble-group">
            <div className="chat-bubble">
              {renderSegments(message.segments, message.text)}
            </div>
            {!message.typing && message.text && <MessageActions message={message} />}
          </div>
        )}
      </div>
    );
  }

  function MessageActions({ message }) {
    const [feedback, setFeedback] = React.useState(null);
    const [copied, setCopied] = React.useState(false);

    const handleCopy = () => {
      const text = message.text || '';
      navigator.clipboard.writeText(text).then(() => {
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      });
    };

    const handleFeedback = (type) => {
      setFeedback((prev) => (prev === type ? null : type));
      // TODO: send feedback to backend
    };

    return (
      <div className="message-actions">
        <button
          type="button"
          className={`msg-action-btn ${copied ? 'active' : ''}`}
          onClick={handleCopy}
          title={copied ? 'Copied!' : 'Copy message'}
          aria-label="Copy message"
        >
          {copied ? (
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="20 6 9 17 4 12" />
            </svg>
          ) : (
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
              <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
            </svg>
          )}
        </button>
        <button
          type="button"
          className={`msg-action-btn ${feedback === 'up' ? 'active' : ''}`}
          onClick={() => handleFeedback('up')}
          title="Helpful"
          aria-label="Mark as helpful"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill={feedback === 'up' ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M14 9V5a3 3 0 0 0-3-3l-4 9v11h11.28a2 2 0 0 0 2-1.7l1.38-9a2 2 0 0 0-2-2.3H14zM7 22H4a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2h3" />
          </svg>
        </button>
        <button
          type="button"
          className={`msg-action-btn ${feedback === 'down' ? 'active' : ''}`}
          onClick={() => handleFeedback('down')}
          title="Not helpful"
          aria-label="Mark as not helpful"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill={feedback === 'down' ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M10 15v4a3 3 0 0 0 3 3l4-9V2H5.72a2 2 0 0 0-2 1.7l-1.38 9a2 2 0 0 0 2 2.3H10zM17 2h2.67A2.31 2.31 0 0 1 22 4v7a2.31 2.31 0 0 1-2.33 2H17" />
          </svg>
        </button>
      </div>
    );
  }

  function VideoBubble({ segment, onVideoClick }) {
    const videoRef = React.useRef(null);

    const handleSeek = (sec) => {
      if (videoRef.current) {
        videoRef.current.currentTime = sec;
        videoRef.current.play();
      }
    };

    const timestamps = segment.timestamps || [];

    return (
      <div className="video-bubble">
        <video
          ref={videoRef}
          className="chat-video"
          controls
          onClick={() => onVideoClick && onVideoClick({ url: segment.url, timestamps })}
        >
          <source src={segment.url} type="video/mp4" />
        </video>
        {timestamps.length > 0 && (
          <div className="video-timestamps">
            {timestamps.map((ts, idx) => (
              <button
                key={idx}
                type="button"
                className="timestamp-chip"
                onClick={() => handleSeek(ts.seconds)}
              >
                {ts.label}
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.ChatMessage = { ChatMessage, MessageActions, VideoBubble };
})();
