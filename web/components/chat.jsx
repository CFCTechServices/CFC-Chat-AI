// Chat page and supporting components
(() => {
  const { Layout } = window.CFC.Layout;
  const { Card } = window.CFC.Primitives;
  const { useUser } = window.CFC.UserContext;
  const { useState, useEffect, useRef, useCallback, useMemo } = React;

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

  // Escape HTML to prevent XSS, then apply safe markdown formatting
  function renderMarkdown(text) {
    if (!text) return '';

    const escapeHtml = (str) =>
      str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');

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

    // Only allow bold/italic — no links, images, or arbitrary HTML
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
  }

  function ChatMessage({ message, onFeedback }) {
    const isUser = message.role === 'user';
    const isAssistant = message.role === 'assistant';

    const html = useMemo(() => renderMarkdown(message.content || ''), [message.content]);

    return (
      <div className={`chat-message ${isUser ? 'user' : 'bot'}`}>
        <div className={`chat-bubble ${isUser ? 'user-bubble' : 'bot-bubble'}`}>
          <div className="chat-text markdown" dangerouslySetInnerHTML={{ __html: html }} />
        </div>
        {isAssistant && message.id && !message.id.startsWith('temp') && (
          <div style={{ marginTop: '4px', display: 'flex', gap: '8px', fontSize: '0.8rem', marginLeft: '4px' }}>
            <button
              className="btn-secondary"
              style={{ padding: '2px 8px', height: '24px' }}
              onClick={() => onFeedback(message.id, 1)}
              aria-label="Thumbs up"
            >
              👍
            </button>
            <button
              className="btn-secondary"
              style={{ padding: '2px 8px', height: '24px' }}
              onClick={() => onFeedback(message.id, -1)}
              aria-label="Thumbs down"
            >
              👎
            </button>
            {message.citations && message.citations.length > 0 && (
              <span style={{ color: 'var(--color-text-muted)', marginLeft: '8px', alignSelf: 'center' }}>
                Sources: {message.citations.map(c => c.source || c.title || 'Doc').slice(0, 2).join(', ')}
              </span>
            )}
          </div>
        )}
      </div>
    );
  }

  let msgCounter = 0;

  function ChatPage() {
    const { session } = useUser();
    const [sessions, setSessions] = useState([]);
    const [currentSessionId, setCurrentSessionId] = useState(null);
    const [messages, setMessages] = useState([]);
    const [input, setInput] = useState('');
    const [sending, setSending] = useState(false);
    const chatThreadRef = useRef(null);

    useEffect(() => {
      if (session) fetchSessions();
    }, [session]);

    useEffect(() => {
      if (session && currentSessionId) {
        fetchMessages(currentSessionId);
      } else {
        setMessages([]);
      }
    }, [session, currentSessionId]);

    useEffect(() => {
      if (chatThreadRef.current) {
        chatThreadRef.current.scrollTop = chatThreadRef.current.scrollHeight;
      }
    }, [messages]);

    const fetchSessions = async () => {
      try {
        const res = await fetch("/api/chat/sessions", {
          headers: { "Authorization": `Bearer ${session.access_token}` },
        });
        if (res.ok) {
          const data = await res.json();
          setSessions(data);
          if (data.length > 0 && !currentSessionId) {
            setCurrentSessionId(data[0].id);
          }
        }
      } catch (e) { console.error("Fetch sessions error", e); }
    };

    const fetchMessages = async (sessionId) => {
      try {
        const res = await fetch(`/api/chat/sessions/${sessionId}`, {
          headers: { "Authorization": `Bearer ${session.access_token}` },
        });
        if (res.ok) {
          const data = await res.json();
          setMessages(data);
        }
      } catch (e) { console.error("Fetch messages error", e); }
    };

    const createNewSession = async () => {
      try {
        const res = await fetch("/api/chat/sessions", {
          method: "POST",
          headers: {
            "Authorization": `Bearer ${session.access_token}`,
            "Content-Type": "application/json"
          },
          body: JSON.stringify({ title: "New Chat" })
        });
        if (res.ok) {
          const newSession = await res.json();
          setSessions([newSession, ...sessions]);
          setCurrentSessionId(newSession.id);
        }
      } catch (e) { console.error("Create session error", e); }
    };

    const handleSend = async (e) => {
      e.preventDefault();
      const q = input.trim();
      if (!q || sending) return;

      let activeSessionId = currentSessionId;
      if (!activeSessionId) {
        try {
          const res = await fetch("/api/chat/sessions", {
            method: "POST",
            headers: {
              "Authorization": `Bearer ${session.access_token}`,
              "Content-Type": "application/json"
            },
            body: JSON.stringify({ title: q.substring(0, 30) })
          });
          if (!res.ok) {
            console.error("Failed to create session");
            return;
          }
          const newSession = await res.json();
          setSessions(prev => [newSession, ...prev]);
          setCurrentSessionId(newSession.id);
          activeSessionId = newSession.id;
        } catch (err) {
          console.error("Create session error", err);
          return;
        }
      }

      msgCounter += 1;
      const userMsg = {
        id: `temp-user-${Date.now()}-${msgCounter}`,
        role: 'user',
        content: q
      };
      setMessages(prev => [...prev, userMsg]);
      setInput('');
      setSending(true);

      try {
        const res = await fetch("/api/chat/message", {
          method: "POST",
          headers: {
            "Authorization": `Bearer ${session.access_token}`,
            "Content-Type": "application/json"
          },
          body: JSON.stringify({ session_id: activeSessionId, content: q })
        });

        if (res.ok) {
          const botMsg = await res.json();
          setMessages(prev => [...prev, botMsg]);
        } else {
          msgCounter += 1;
          setMessages(prev => [...prev, {
            id: `temp-err-${Date.now()}-${msgCounter}`,
            role: 'assistant',
            content: "Error: Could not retrieve answer."
          }]);
        }
      } catch (err) {
        console.error(err);
        msgCounter += 1;
        setMessages(prev => [...prev, {
          id: `temp-err-${Date.now()}-${msgCounter}`,
          role: 'assistant',
          content: "Error: " + err.message
        }]);
      } finally {
        setSending(false);
      }
    };

    const handleFeedback = async (msgId, rating) => {
      try {
        await fetch("/api/chat/feedback", {
          method: "POST",
          headers: {
            "Authorization": `Bearer ${session.access_token}`,
            "Content-Type": "application/json"
          },
          body: JSON.stringify({ message_id: msgId, session_id: currentSessionId, rating })
        });
      } catch (e) { console.error("Feedback error", e); }
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

          <div style={{ display: 'flex', gap: '20px', alignItems: 'flex-start', height: 'calc(100vh - 220px)' }}>
            {/* Sidebar */}
            <div style={{ width: '260px', flexShrink: 0, height: '100%', display: 'flex', flexDirection: 'column' }}>
              <Card style={{ height: '100%', display: 'flex', flexDirection: 'column', padding: '12px' }}>
                <button className="btn-secondary" style={{ width: '100%', marginBottom: '10px' }} onClick={createNewSession}>+ New Chat</button>
                <nav style={{ flex: 1, overflowY: 'auto', paddingRight: '4px' }} aria-label="Chat sessions">
                  {sessions.map(s => (
                    <button
                      key={s.id}
                      type="button"
                      onClick={() => setCurrentSessionId(s.id)}
                      aria-current={currentSessionId === s.id ? 'true' : undefined}
                      style={{
                        display: 'block',
                        width: '100%',
                        textAlign: 'left',
                        padding: '8px 10px',
                        cursor: 'pointer',
                        backgroundColor: currentSessionId === s.id ? 'var(--color-surface-secondary)' : 'transparent',
                        borderRadius: '8px',
                        marginBottom: '4px',
                        fontSize: '0.9rem',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        border: currentSessionId === s.id ? '1px solid var(--color-border)' : '1px solid transparent',
                        color: 'inherit',
                        font: 'inherit',
                      }}
                    >
                      {s.title || "New Chat"}
                    </button>
                  ))}
                </nav>
              </Card>
            </div>

            {/* Main Chat Area */}
            <div style={{ flex: 1, height: '100%', minWidth: 0 }}>
              <Card className="chat-card" style={{ height: '100%' }}>
                <div className="chat-thread" ref={chatThreadRef} role="log" aria-live="polite">
                  {messages.length === 0 && (
                    <div style={{ textAlign: 'center', marginTop: '40px', color: 'var(--color-text-muted)' }}>
                      Start a conversation...
                    </div>
                  )}
                  {messages.map((m) => (
                    <ChatMessage
                      key={m.id}
                      message={m}
                      onFeedback={handleFeedback}
                    />
                  ))}
                  {sending && (
                    <div className="chat-message bot" aria-label="Assistant is typing">
                      <div className="chat-bubble typing-bubble">
                        <div className="typing-dots-container">
                          <span className="typing-dot" />
                          <span className="typing-dot" />
                          <span className="typing-dot" />
                        </div>
                      </div>
                    </div>
                  )}
                </div>
                <form className="chat-composer" onSubmit={handleSend} style={{ marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid var(--color-border)' }}>
                  <div className="composer-row" style={{ display: 'flex', gap: '8px' }}>
                    <input
                      type="text"
                      className="composer-input"
                      style={{ flex: 1, borderRadius: '999px', padding: '10px 16px', border: '1px solid var(--color-border)', outline: 'none' }}
                      placeholder="Ask anything..."
                      value={input}
                      onChange={(e) => setInput(e.target.value)}
                      aria-label="Chat message input"
                    />
                    <button type="submit" className="btn-primary composer-button" disabled={sending}>
                      {sending ? 'Sending…' : 'Send'}
                    </button>
                  </div>
                </form>
              </Card>
            </div>
          </div>
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.ChatPage = ChatPage;
})();
