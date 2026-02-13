// Chat page – orchestrates sidebar, thread, and composer
(() => {
  const { Layout } = window.CFC.Layout;
  const { ChatSidebar } = window.CFC.ChatSidebar;
  const { ChatMessage } = window.CFC.ChatMessage;
  const { ChatComposer } = window.CFC.ChatComposer;
  const { useModal, buildVideoSegmentsFromAnswer, buildImageSegmentsFromAnswer } = window.CFC.ChatUtils;

  function ChatPage() {
    const initialChatId = React.useRef(`chat-${Date.now()}`).current;
    const [messages, setMessages] = React.useState([]);
    const [input, setInput] = React.useState('');
    const [sending, setSending] = React.useState(false);
    const [attachedImages, setAttachedImages] = React.useState([]);
    const [chatHistory, setChatHistory] = React.useState([
      { id: initialChatId, title: 'Welcome Chat', messages: [] },
    ]);
    const [activeChatId, setActiveChatId] = React.useState(initialChatId);
    const chatThreadRef = React.useRef(null);
    const thinkingTimeoutsRef = React.useRef({});

    const { open: openModal, modal } = useModal();

    // ---- Random welcome phrase for empty chat ----
    const welcomePhrases = [
      'Chat with CFC AI',
      'What can CFC AI help with today?',
      'Ready when you are',
      'Ask anything about CFC software…',
      'How can I help you today?',
    ];
    const welcomePhrase = React.useMemo(
      () => welcomePhrases[Math.floor(Math.random() * welcomePhrases.length)],
      [activeChatId],
    );

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

    // ---- Sync active chat into sidebar history ----
    React.useEffect(() => {
      const nonTyping = messages.filter((m) => !m.typing);
      if (nonTyping.length === 0) return;
      const firstUserMsg = nonTyping.find((m) => m.role === 'user');
      const title = firstUserMsg ? firstUserMsg.text.slice(0, 40) : 'New Chat';
      setChatHistory((prev) =>
        prev.map((c) => (c.id === activeChatId ? { ...c, title, messages: nonTyping } : c)),
      );
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

    // ---- Submit question ----
    const handleSubmit = async (e) => {
      e.preventDefault();
      const q = input.trim();
      if (!q || sending) return;

      const imageSegs = attachedImages.map((img) => ({ type: 'image', url: img.url, alt: 'Attached image' }));
      appendMessage({
        id: `u-${Date.now()}`,
        role: 'user',
        text: q,
        segments: [{ type: 'text', text: q }, ...imageSegs],
      });

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
        if (!data.success) throw new Error(data.detail || 'Error from assistant');

        const answer = data.answer || 'No answer available.';
        const videoSegments = buildVideoSegmentsFromAnswer(data);
        const answerImages = buildImageSegmentsFromAnswer(data);
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
    const handleNewChat = () => {
      const newId = `chat-${Date.now()}`;
      setChatHistory((prev) => [{ id: newId, title: 'New Chat', messages: [] }, ...prev]);
      setActiveChatId(newId);
      setMessages([]);
      setInput('');
      setAttachedImages([]);
    };

    const handleSelectChat = (chatId) => {
      if (chatId === activeChatId) return;
      const selected = chatHistory.find((c) => c.id === chatId);
      if (selected) {
        setActiveChatId(chatId);
        setMessages(selected.messages || []);
        setInput('');
        setAttachedImages([]);
      }
    };

    const handleDeleteChat = (chatId) => {
      const remaining = chatHistory.filter((c) => c.id !== chatId);
      if (remaining.length === 0) {
        // Always keep at least one chat
        const newId = `chat-${Date.now()}`;
        setChatHistory([{ id: newId, title: 'New Chat', messages: [] }]);
        setActiveChatId(newId);
        setMessages([]);
      } else if (chatId === activeChatId) {
        // Deleted the active chat – switch to the first remaining one
        setChatHistory(remaining);
        setActiveChatId(remaining[0].id);
        setMessages(remaining[0].messages || []);
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
