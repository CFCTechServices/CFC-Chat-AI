// Chat page and supporting components
(() => {
  const { Layout } = window.CFC.Layout;
  const { Card } = window.CFC.Primitives;

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
        <div className="chat-bubble">
          {message.segments && message.segments.length ? (
            message.segments.map((seg, idx) => {
              if (seg.type === 'text') {
                return <MarkdownText key={idx} text={seg.text} />;
              }
              if (seg.type === 'image') {
                return (
                  <img
                    key={idx}
                    src={seg.url}
                    alt={seg.alt || 'Image'}
                    className="chat-image"
                    onClick={() => onImageClick && onImageClick(seg.url)}
                  />
                );
              }
              if (seg.type === 'video') {
                return <VideoBubble key={idx} segment={seg} onVideoClick={onVideoClick} />;
              }
              return null;
            })
          ) : (
            <MarkdownText text={message.text} />
          )}
        </div>
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

  function ChatPage() {
    const [messages, setMessages] = React.useState([]);
    const [input, setInput] = React.useState('');
    const [sending, setSending] = React.useState(false);
    const [attachedImages, setAttachedImages] = React.useState([]);
    const chatThreadRef = React.useRef(null);
    const thinkingTimeoutsRef = React.useRef({});

    const { open: openModal, modal } = useModal();

    const prepareConversationHistory = (msgs, maxMessages = 8) => {
      const filtered = msgs.filter((m) => m.role === 'user' || m.role === 'assistant');
      if (filtered.length <= maxMessages) return filtered;
      const firstCount = Math.min(3, Math.floor(filtered.length / 3));
      const firstMessages = filtered.slice(0, firstCount);
      const recentCount = maxMessages - firstCount;
      const recentMessages = filtered.slice(-recentCount);
      const combined = [...firstMessages];
      const firstIds = new Set(firstMessages.map((m) => m.id));
      for (const msg of recentMessages) {
        if (!firstIds.has(msg.id)) combined.push(msg);
      }
      return combined;
    };

    React.useEffect(() => {
      if (chatThreadRef.current) {
        chatThreadRef.current.scrollTop = chatThreadRef.current.scrollHeight;
      }
    }, [messages]);

    React.useEffect(() => {
      messages.forEach((msg) => {
        if (msg.typing && !msg.showThinking) {
          if (!thinkingTimeoutsRef.current[msg.id]) {
            thinkingTimeoutsRef.current[msg.id] = setTimeout(() => {
              setMessages((prev) => {
                const currentMsg = prev.find((m) => m.id === msg.id);
                if (currentMsg && currentMsg.typing) {
                  return prev.map((m) => (m.id === msg.id ? { ...m, showThinking: true } : m));
                }
                return prev;
              });
            }, 7000);
          }
        } else if (!msg.typing) {
          if (thinkingTimeoutsRef.current[msg.id]) {
            clearTimeout(thinkingTimeoutsRef.current[msg.id]);
            delete thinkingTimeoutsRef.current[msg.id];
          }
          if (msg.showThinking) {
            setMessages((prev) => prev.map((m) => (m.id === msg.id ? { ...m, showThinking: false } : m)));
          }
        }
      });

      return () => {
        Object.values(thinkingTimeoutsRef.current).forEach((timeout) => { if (timeout) clearTimeout(timeout); });
        thinkingTimeoutsRef.current = {};
      };
    }, [messages]);

    const handleImageChange = (e) => {
      const files = Array.from(e.target.files || []);
      const previews = files.map((file) => ({ file, url: URL.createObjectURL(file), id: `${file.name}-${Math.random().toString(36).slice(2)}` }));
      setAttachedImages((prev) => [...prev, ...previews]);
    };

    const appendMessage = (msg) => {
      setMessages((prev) => [...prev, msg]);
    };

    const simulateStreaming = (fullText, baseMessageId, extraSegments = []) => {
      const chars = Array.from(fullText);
      let idx = 0;
      const speed = 18;

      const imageSegments = extraSegments.filter((seg) => seg.type === 'image');
      const otherSegments = extraSegments.filter((seg) => seg.type !== 'image');
      const sortedImages = [...imageSegments].sort((a, b) => {
        const posA = a.position ?? -1;
        const posB = b.position ?? -1;
        if (posA === -1) return 1;
        if (posB === -1) return -1;
        return posA - posB;
      });

      const buildSegments = (textLength) => {
        const segments = [];
        let textStart = 0;
        const insertedImages = new Set();

        for (const img of sortedImages) {
          const imgPos = img.position ?? -1;
          if (imgPos === -1 || imgPos >= textLength) continue;
          if (imgPos > textStart) {
            const textBefore = fullText.slice(textStart, imgPos);
            if (textBefore.trim()) segments.push({ type: 'text', text: textBefore });
          }
          segments.push(img);
          insertedImages.add(img.path || img.url);
          textStart = imgPos;
        }

        if (textStart < textLength) {
          const remainingText = fullText.slice(textStart, textLength);
          if (remainingText.trim()) segments.push({ type: 'text', text: remainingText });
        }

        for (const img of sortedImages) {
          if (!insertedImages.has(img.path || img.url)) segments.push(img);
        }

        segments.push(...otherSegments);
        return segments;
      };

      const interval = setInterval(() => {
        idx += 3;
        const currentLength = Math.min(idx, chars.length);
        const segments = buildSegments(currentLength);

        setMessages((prev) =>
          prev.map((m) =>
            m.id === baseMessageId
              ? { ...m, typing: false, text: fullText.slice(0, currentLength), segments }
              : m,
          ),
        );
        if (chatThreadRef.current) {
          chatThreadRef.current.scrollTop = chatThreadRef.current.scrollHeight;
        }
        if (idx >= chars.length) {
          clearInterval(interval);
        }
      }, speed);
    };

    const handleSubmit = async (e) => {
      e.preventDefault();
      const q = input.trim();
      if (!q || sending) return;

      const imageSegments = attachedImages.map((img) => ({ type: 'image', url: img.url, alt: 'Attached image' }));

      const userMessage = {
        id: `u-${Date.now()}`,
        role: 'user',
        text: q,
        segments: [{ type: 'text', text: q }, ...imageSegments],
      };
      appendMessage(userMessage);

      const imagesForBackend = attachedImages.map((img) => img.url);

      setInput('');
      setAttachedImages([]);
      setSending(true);

      const botId = `b-${Date.now()}`;
      appendMessage({ id: botId, role: 'assistant', text: '', segments: [], typing: true });

      try {
        const conversationHistory = prepareConversationHistory(
          messages.filter((m) => m.role === 'user' || m.role === 'assistant'),
        ).map((m) => ({ role: m.role, content: m.text }));

        const res = await fetch('/ask', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            question: q + (imagesForBackend.length ? ` (images: ${imagesForBackend.join(', ')})` : ''),
            top_k: 4,
            conversation_history: conversationHistory.length > 0 ? conversationHistory : undefined,
          }),
        });
        const data = await res.json();
        if (!data.success) {
          throw new Error(data.detail || 'Error from assistant');
        }

        const answer = data.answer || 'No answer available.';
        const videoSegments = buildVideoSegmentsFromAnswer(data);
        const answerImages = buildImageSegmentsFromAnswer(data);
        const allSegments = [...answerImages, ...videoSegments];
        simulateStreaming(answer, botId, allSegments);
      } catch (err) {
        setMessages((prev) =>
          prev.map((m) =>
            m.id === botId
              ? {
                  ...m,
                  typing: false,
                  text: err.message || String(err),
                  segments: [{ type: 'text', text: err.message || String(err) }],
                }
              : m,
          ),
        );
      } finally {
        setSending(false);
      }
    };

    const handleImageClick = (url) => {
      openModal(<img src={url} alt="Preview" className="modal-image" />);
    };

    const handleVideoClick = ({ url, timestamps }) => {
      openModal(
        <div className="modal-video-wrapper">
          <video src={url} className="modal-video" controls autoPlay />
          {timestamps && timestamps.length > 0 && (
            <div className="video-timestamps">
              {timestamps.map((ts, idx) => (
                <span key={idx} className="timestamp-chip static">{ts.label}</span>
              ))}
            </div>
          )}
        </div>,
      );
    };

    return (
      <Layout>
        <div className="page chat-page">
          <div className="page-header-row">
            <div>
              <h1>Chat with CFC AI</h1>
              <p>Ask questions about your software and get quick, informed answers.</p>
            </div>
          </div>

          <Card className="chat-card">
            <div className="chat-thread" id="chatThread" ref={chatThreadRef}>
              {messages.map((m) => (
                <ChatMessage key={m.id} message={m} onImageClick={handleImageClick} onVideoClick={handleVideoClick} />
              ))}
            </div>
            <form className="chat-composer" onSubmit={handleSubmit}>
              <div className="composer-row">
                <button
                  type="button"
                  className={`composer-info-icon ${messages.length > 12 ? 'visible' : ''}`}
                  onClick={() => openModal(
                    <div className="conversation-info-modal">
                      <h3>Long Conversation Notice</h3>
                      <p>
                        This conversation is getting long. CFC AI may begin to lose sight of the original goal in very long conversations.
                      </p>
                      <p>
                        For separate questions or new topics, consider refreshing the page to start a new conversation. This ensures the most reliable and focused responses.
                      </p>
                    </div>,
                  )}
                  title="Conversation length info"
                  aria-label="Conversation length information"
                >
                  <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
                    <circle cx="10" cy="10" r="9" stroke="currentColor" strokeWidth="1.5" fill="none" />
                    <path d="M10 7V10" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
                    <circle cx="10" cy="13" r="1" fill="currentColor" />
                  </svg>
                </button>
                <input
                  type="text"
                  className="composer-input"
                  placeholder="Ask anything about CFC software…"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                />
                <label className="btn-primary composer-button" title="Attach images">
                  <span>Attach</span>
                  <input type="file" accept="image/*" multiple onChange={handleImageChange} />
                </label>
                <button type="submit" className="btn-primary composer-button" disabled={sending}>
                  {sending ? 'Sending…' : 'Send'}
                </button>
              </div>
              {attachedImages.length > 0 && (
                <div className="attached-images">
                  {attachedImages.map((img) => (
                    <img
                      key={img.id}
                      src={img.url}
                      alt="Attachment"
                      className="attached-thumb"
                      onClick={() => handleImageClick(img.url)}
                    />
                  ))}
                </div>
              )}
            </form>
          </Card>
        </div>
        {modal}
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.ChatPage = ChatPage;
})();
