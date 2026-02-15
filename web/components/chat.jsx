// Chat page – orchestrates sidebar, thread, and composer
(() => {
  const { Layout } = window.CFC.Layout;
  const { ChatSidebar } = window.CFC.ChatSidebar;
  const { ChatMessage } = window.CFC.ChatMessage;
  const { ChatComposer } = window.CFC.ChatComposer;
  const { useModal, buildVideoSegmentsFromAnswer, buildImageSegmentsFromAnswer } = window.CFC.ChatUtils;
  const { useUser } = window.CFC.UserContext;

  // Helper: build auth headers
  function authHeaders(token) {
    return {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`,
    };
  }

  // Convert DB message rows into the UI message shape
  function dbMsgToUI(msg) {
    return {
      id: msg.id,
      role: msg.role,
      text: msg.content,
      segments: [{ type: 'text', text: msg.content }],
    };
  }

  function ChatPage() {
    const { session } = useUser();
    const { routeParams } = window.CFC.RouterContext.useRouter();
    const token = session?.access_token;

    const [messages, setMessages] = React.useState([]);
    const [input, setInput] = React.useState('');
    const [sending, setSending] = React.useState(false);
    const [attachedImages, setAttachedImages] = React.useState([]);
    const [chatHistory, setChatHistory] = React.useState([]);
    const [activeChatId, setActiveChatId] = React.useState(null);
    const [loadingSessions, setLoadingSessions] = React.useState(true);
    const chatThreadRef = React.useRef(null);
    const thinkingTimeoutsRef = React.useRef({});

    const { open: openModal, modal } = useModal();

    // ---- Random welcome phrase for empty chat ----
    const welcomePhrases = [
      'Chat with CFC AI',
      'What can CFC AI help with today?',
      'Ready when you are',
      'Ask anything about CFC software\u2026',
      'How can I help you today?',
    ];
    const welcomePhrase = React.useMemo(
      () => welcomePhrases[Math.floor(Math.random() * welcomePhrases.length)],
      [activeChatId],
    );

    // ---- Load sessions from DB on mount ----
    React.useEffect(() => {
      if (!token) return;
      setLoadingSessions(true);
      fetch('/api/chat/sessions', { headers: authHeaders(token) })
        .then((res) => (res.ok ? res.json() : []))
        .then((data) => {
          const mapped = data.map((s) => ({
            id: s.id,
            title: s.title || 'Untitled Chat',
            messages: [], // loaded on select
          }));
          setChatHistory(mapped);

          // If navigated from history with a specific sessionId, open that one
          const targetId = routeParams?.sessionId;
          const targetExists = targetId && mapped.some((s) => s.id === targetId);
          const selected = targetExists ? targetId : (mapped.length > 0 ? mapped[0].id : null);

          if (selected) {
            setActiveChatId(selected);
            loadSessionMessages(selected);
          }
        })
        .catch(() => setChatHistory([]))
        .finally(() => setLoadingSessions(false));
    }, [token]);

    // ---- Load messages for a session from DB ----
    const loadSessionMessages = async (sessionId) => {
      if (!token) return;
      try {
        const res = await fetch(`/api/chat/sessions/${sessionId}`, {
          headers: authHeaders(token),
        });
        if (!res.ok) return;
        const data = await res.json();
        const uiMsgs = data.map(dbMsgToUI);
        setMessages(uiMsgs);
        // Update cached messages in sidebar history
        setChatHistory((prev) =>
          prev.map((c) => (c.id === sessionId ? { ...c, messages: uiMsgs } : c)),
        );
      } catch {
        // ignore
      }
    };

    // ---- Conversation history for API context ----
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

    // ---- Auto-scroll on new messages ----
    React.useEffect(() => {
      if (chatThreadRef.current) {
        chatThreadRef.current.scrollTop = chatThreadRef.current.scrollHeight;
      }
    }, [messages]);

    // ---- Update sidebar title when messages change ----
    React.useEffect(() => {
      const nonTyping = messages.filter((m) => !m.typing);
      if (nonTyping.length === 0) return;
      const firstUserMsg = nonTyping.find((m) => m.role === 'user');
      const title = firstUserMsg ? firstUserMsg.text.slice(0, 40) : 'New Chat';
      setChatHistory((prev) =>
        prev.map((c) => (c.id === activeChatId ? { ...c, title, messages: nonTyping } : c)),
      );
      // Persist title to DB (fire-and-forget)
      if (token && activeChatId && firstUserMsg) {
        fetch(`/api/chat/sessions/${activeChatId}`, {
          method: 'PATCH',
          headers: authHeaders(token),
          body: JSON.stringify({ title }),
        }).catch(() => {});
      }
    }, [messages, activeChatId]);

    // ---- "Thinking" indicator after 7s ----
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
        Object.values(thinkingTimeoutsRef.current).forEach((t) => { if (t) clearTimeout(t); });
        thinkingTimeoutsRef.current = {};
      };
    }, [messages]);

    // ---- Helpers ----
    const appendMessage = (msg) => setMessages((prev) => [...prev, msg]);

    const handleImageChange = (e) => {
      const files = Array.from(e.target.files || []);
      const previews = files.map((file) => ({
        file,
        url: URL.createObjectURL(file),
        id: `${file.name}-${Math.random().toString(36).slice(2)}`,
      }));
      setAttachedImages((prev) => [...prev, ...previews]);
    };

    // ---- Streaming simulation ----
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
        if (idx >= chars.length) clearInterval(interval);
      }, speed);
    };

    // ---- Ensure we have an active DB session, creating one if needed ----
    const ensureSession = async () => {
      if (activeChatId) return activeChatId;
      // Create a new session in DB
      const res = await fetch('/api/chat/sessions', {
        method: 'POST',
        headers: authHeaders(token),
        body: JSON.stringify({ title: 'New Chat' }),
      });
      if (!res.ok) throw new Error('Failed to create chat session');
      const sess = await res.json();
      setChatHistory((prev) => [{ id: sess.id, title: sess.title || 'New Chat', messages: [] }, ...prev]);
      setActiveChatId(sess.id);
      return sess.id;
    };

    // ---- Submit question ----
    const handleSubmit = async (e) => {
      e.preventDefault();
      const q = input.trim();
      if (!q || sending || !token) return;

      const imageSegs = attachedImages.map((img) => ({ type: 'image', url: img.url, alt: 'Attached image' }));
      appendMessage({
        id: `u-${Date.now()}`,
        role: 'user',
        text: q,
        segments: [{ type: 'text', text: q }, ...imageSegs],
      });

      setInput('');
      setAttachedImages([]);
      setSending(true);

      const botId = `b-${Date.now()}`;
      appendMessage({ id: botId, role: 'assistant', text: '', segments: [], typing: true });

      try {
        const sessionId = await ensureSession();

        const res = await fetch('/api/chat/message', {
          method: 'POST',
          headers: authHeaders(token),
          body: JSON.stringify({
            session_id: sessionId,
            content: q,
          }),
        });
        const data = await res.json();
        if (!res.ok) throw new Error(data.detail || 'Error from assistant');

        const answer = data.content || 'No answer available.';
        const citations = data.citations || [];
        // Build image/video segments from citations metadata if available
        const videoSegments = buildVideoSegmentsFromAnswer({ context_used: citations });
        const answerImages = buildImageSegmentsFromAnswer({ relevant_images: citations.flatMap(c => c.image_paths || []).map(p => ({ path: p })) });
        simulateStreaming(answer, botId, [...answerImages, ...videoSegments]);
      } catch (err) {
        setMessages((prev) =>
          prev.map((m) =>
            m.id === botId
              ? { ...m, typing: false, text: err.message || String(err), segments: [{ type: 'text', text: err.message || String(err) }] }
              : m,
          ),
        );
      } finally {
        setSending(false);
      }
    };

    // ---- Modal openers ----
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

    const handleLongConvoInfo = () => {
      openModal(
        <div className="conversation-info-modal">
          <h3>Long Conversation Notice</h3>
          <p>This conversation is getting long. CFC AI may begin to lose sight of the original goal in very long conversations.</p>
          <p>For separate questions or new topics, consider refreshing the page to start a new conversation. This ensures the most reliable and focused responses.</p>
        </div>,
      );
    };

    // ---- Sidebar handlers ----
    const handleNewChat = async () => {
      if (!token) return;
      try {
        const res = await fetch('/api/chat/sessions', {
          method: 'POST',
          headers: authHeaders(token),
          body: JSON.stringify({ title: 'New Chat' }),
        });
        if (!res.ok) return;
        const sess = await res.json();
        const newEntry = { id: sess.id, title: sess.title || 'New Chat', messages: [] };
        setChatHistory((prev) => [newEntry, ...prev]);
        setActiveChatId(sess.id);
        setMessages([]);
        setInput('');
        setAttachedImages([]);
      } catch {
        // ignore
      }
    };

    const handleSelectChat = async (chatId) => {
      if (chatId === activeChatId) return;
      setActiveChatId(chatId);
      setInput('');
      setAttachedImages([]);
      // Check if we already have messages cached
      const cached = chatHistory.find((c) => c.id === chatId);
      if (cached && cached.messages && cached.messages.length > 0) {
        setMessages(cached.messages);
      } else {
        setMessages([]);
        await loadSessionMessages(chatId);
      }
    };

    const handleDeleteChat = async (chatId) => {
      if (!token) return;
      // Delete from DB
      try {
        await fetch(`/api/chat/sessions/${chatId}`, {
          method: 'DELETE',
          headers: authHeaders(token),
        });
      } catch {
        // continue with local removal even if API fails
      }

      const remaining = chatHistory.filter((c) => c.id !== chatId);
      if (remaining.length === 0) {
        setChatHistory([]);
        setActiveChatId(null);
        setMessages([]);
      } else if (chatId === activeChatId) {
        setChatHistory(remaining);
        setActiveChatId(remaining[0].id);
        // Load messages for the new active chat
        const cached = remaining[0];
        if (cached.messages && cached.messages.length > 0) {
          setMessages(cached.messages);
        } else {
          setMessages([]);
          loadSessionMessages(remaining[0].id);
        }
      } else {
        setChatHistory(remaining);
      }
      setInput('');
      setAttachedImages([]);
    };

    // ---- Render ----
    return (
      <Layout fullWidth>
        <div className="chat-layout">
          <ChatSidebar
            chatHistory={chatHistory}
            activeChatId={activeChatId}
            onNewChat={handleNewChat}
            onSelectChat={handleSelectChat}
            onDeleteChat={handleDeleteChat}
          />

          <section className="chat-main">
            <div className="chat-thread" id="chatThread" ref={chatThreadRef}>
              {messages.length === 0 && (
                <div className="chat-empty-state">
                  <h1>{welcomePhrase}</h1>
                  <p>Ask questions about your software and get quick, informed answers.</p>
                </div>
              )}
              {messages.map((m) => (
                <ChatMessage key={m.id} message={m} onImageClick={handleImageClick} onVideoClick={handleVideoClick} />
              ))}
            </div>

            <ChatComposer
              input={input}
              onInputChange={setInput}
              onSubmit={handleSubmit}
              sending={sending}
              attachedImages={attachedImages}
              onImageChange={handleImageChange}
              onImageClick={handleImageClick}
              onLongConvoInfo={handleLongConvoInfo}
              showLongConvoWarning={messages.length > 12}
            />
          </section>
        </div>
        {modal}
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.ChatPage = ChatPage;
})();
