// Chat sidebar – manages chat history list and new-chat action
(() => {
  function ChatSidebar({
    chatHistory,
    activeChatId,
    onNewChat,
    onSelectChat,
    onDeleteChat,
    onTogglePin,
    onToggleFavorite,
  }) {
    const [openMenuId, setOpenMenuId] = React.useState(null);

    React.useEffect(() => {
      const close = (event) => {
        if (!event.target.closest('.sidebar-chat-actions')) {
          setOpenMenuId(null);
        }
      };
      window.addEventListener('click', close);
      return () => window.removeEventListener('click', close);
    }, []);

    const orderedChats = React.useMemo(() => {
      return [...chatHistory].sort((a, b) => {
        const pinRank = Number(Boolean(b.pinned)) - Number(Boolean(a.pinned));
        if (pinRank !== 0) return pinRank;
        const favRank = Number(Boolean(b.favorite)) - Number(Boolean(a.favorite));
        if (favRank !== 0) return favRank;
        return 0;
      });
    }, [chatHistory]);

    return (
      <aside className="chat-sidebar">
        <button type="button" className="btn-primary sidebar-new-chat" onClick={onNewChat}>
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <line x1="8" y1="3" x2="8" y2="13" />
            <line x1="3" y1="8" x2="13" y2="8" />
          </svg>
          New Chat
        </button>
        <div className="sidebar-history">
          {orderedChats.map((chat) => (
            <div
              key={chat.id}
              className={`sidebar-chat-item ${chat.id === activeChatId ? 'active' : ''}`}
              onClick={() => onSelectChat(chat.id)}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => { if (e.key === 'Enter') onSelectChat(chat.id); }}
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
              </svg>
              <span className="sidebar-chat-title">
                {chat.pinned ? '📌 ' : ''}
                {chat.favorite ? '★ ' : ''}
                {chat.title}
              </span>
              <div className="sidebar-chat-actions" onClick={(e) => e.stopPropagation()}>
                <button
                  type="button"
                  className="sidebar-chat-menu-trigger"
                  title="Chat options"
                  onClick={(e) => {
                    e.stopPropagation();
                    setOpenMenuId((prev) => (prev === chat.id ? null : chat.id));
                  }}
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <circle cx="12" cy="5" r="2" />
                    <circle cx="12" cy="12" r="2" />
                    <circle cx="12" cy="19" r="2" />
                  </svg>
                </button>

                {openMenuId === chat.id && (
                  <div className="sidebar-chat-menu" role="menu">
                    <button
                      type="button"
                      className="sidebar-chat-menu-item"
                      onClick={() => {
                        if (onTogglePin) onTogglePin(chat.id);
                        setOpenMenuId(null);
                      }}
                    >
                      {chat.pinned ? 'Unpin' : 'Pin'}
                    </button>
                    <button
                      type="button"
                      className="sidebar-chat-menu-item"
                      onClick={() => {
                        if (onToggleFavorite) onToggleFavorite(chat.id);
                        setOpenMenuId(null);
                      }}
                    >
                      {chat.favorite ? 'Unfavorite' : 'Favorite'}
                    </button>
                    <button
                      type="button"
                      className="sidebar-chat-menu-item danger"
                      onClick={() => {
                        onDeleteChat(chat.id);
                        setOpenMenuId(null);
                      }}
                    >
                      Delete
                    </button>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
        <div className="sidebar-footer">
          <div className="sidebar-count">
            {chatHistory.length} chat{chatHistory.length !== 1 ? 's' : ''}
          </div>
        </div>
      </aside>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.ChatSidebar = { ChatSidebar };
})();
