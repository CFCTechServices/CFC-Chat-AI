// Chat sidebar – manages chat history list and new-chat action
(() => {
  function ChatSidebar({ chatHistory, activeChatId, onNewChat, onSelectChat }) {
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
          {chatHistory.map((chat) => (
            <button
              key={chat.id}
              type="button"
              className={`sidebar-chat-item ${chat.id === activeChatId ? 'active' : ''}`}
              onClick={() => onSelectChat(chat.id)}
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
              </svg>
              <span className="sidebar-chat-title">{chat.title}</span>
            </button>
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
