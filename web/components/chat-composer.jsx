// Chat composer – input bar, attach button, send button
(() => {
  const { Card } = window.CFC.Primitives;

  function ChatComposer({ input, onInputChange, onSubmit, sending, attachedImages, onImageChange, onImageClick, onLongConvoInfo, showLongConvoWarning }) {
    return (
      <Card className="chat-composer-card">
        <form className="chat-composer" onSubmit={onSubmit}>
          <div className="composer-row">
            <button
              type="button"
              className={`composer-info-icon ${showLongConvoWarning ? 'visible' : ''}`}
              onClick={onLongConvoInfo}
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
              onChange={(e) => onInputChange(e.target.value)}
            />
            <label className="btn-primary composer-button" title="Attach images">
              <span>Attach</span>
              <input type="file" accept="image/*" multiple onChange={onImageChange} />
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
                  onClick={() => onImageClick(img.url)}
                />
              ))}
            </div>
          )}
        </form>
      </Card>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.ChatComposer = { ChatComposer };
})();
