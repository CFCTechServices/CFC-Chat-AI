// Chat History page
(() => {
  const { Layout } = window.CFC.Layout;
  const { Card } = window.CFC.Primitives;

  function formatRelative(dateMs) {
    const diff = Date.now() - dateMs;
    const mins = Math.floor(diff / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins}m ago`;
    const hours = Math.floor(mins / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    return `${days}d ago`;
  }

  function useHistoryData() {
    const [items, setItems] = React.useState(() => {
      try {
        const raw = window.localStorage.getItem('cfc-history');
        if (raw) return JSON.parse(raw);
      } catch {}
      // Fallback demo data
      return [
        {
          id: 'welcome',
          title: 'Welcome Chat',
          preview: 'Hello! How can I help you today?',
          messageCount: 1,
          updatedAt: Date.now() - 24 * 60 * 1000,
        },
      ];
    });

    const save = (next) => {
      setItems(next);
      try { window.localStorage.setItem('cfc-history', JSON.stringify(next)); } catch {}
    };

    return { items, setItems: save };
  }

  function HistoryToolbar({ query, onQuery, range, onRange }) {
    return (
      <div className="history-toolbar">
        <div className="history-search">
          <span className="search-icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="11" cy="11" r="8" />
              <line x1="21" y1="21" x2="16.65" y2="16.65" />
            </svg>
          </span>
          <input
            type="text"
            className="history-search-input"
            placeholder="Search conversations..."
            value={query}
            onChange={(e) => onQuery(e.target.value)}
          />
        </div>

        <div className="history-filter">
          <span className="filter-icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polygon points="22 3 2 3 10 12 10 19 14 21 14 12 22 3" />
            </svg>
          </span>
          <select className="history-filter-select" value={range} onChange={(e) => onRange(e.target.value)}>
            <option value="all">All Time</option>
            <option value="24h">Last 24h</option>
            <option value="7d">Last 7 days</option>
            <option value="30d">Last 30 days</option>
          </select>
        </div>
      </div>
    );
  }

  function HistoryItem({ item }) {
    return (
      <div className="history-item">
        <div className="history-item-title">{item.title}</div>
        <div className="history-item-preview">{item.preview}</div>
        <div className="history-item-meta">
          <span className="meta-pill">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10" />
              <polyline points="12 7 12 12 16 14" />
            </svg>
            {formatRelative(item.updatedAt)}
          </span>
          <span className="meta-pill">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 15a4 4 0 0 1-4 4H7l-4 4V5a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4z" />
            </svg>
            {item.messageCount} messages
          </span>
        </div>
      </div>
    );
  }

  function HistoryPage() {
    const { items } = useHistoryData();
    const [query, setQuery] = React.useState('');
    const [range, setRange] = React.useState('all');

    const filtered = React.useMemo(() => {
      const now = Date.now();
      const withinRange = (ts) => {
        if (range === 'all') return true;
        if (range === '24h') return now - ts <= 24 * 60 * 60 * 1000;
        if (range === '7d') return now - ts <= 7 * 24 * 60 * 60 * 1000;
        if (range === '30d') return now - ts <= 30 * 24 * 60 * 60 * 1000;
        return true;
      };
      const q = query.trim().toLowerCase();
      return items.filter((it) => withinRange(it.updatedAt) && (
        !q || it.title.toLowerCase().includes(q) || it.preview.toLowerCase().includes(q)
      ));
    }, [items, query, range]);

    return (
      <Layout>
        <div className="page history-page">
          <div className="page-header-row">
            <div>
              <h1>Chat History</h1>
              <p>Manage your conversations – search, rename, export, or delete</p>
            </div>
          </div>

          <Card>
            <HistoryToolbar query={query} onQuery={setQuery} range={range} onRange={setRange} />
            <div className="history-list">
              {filtered.length === 0 ? (
                <div className="content-empty-state" style={{ minHeight: 180 }}>
                  <div className="empty-state-content">
                    <div className="empty-state-icon">
                      <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                        <circle cx="12" cy="12" r="9" />
                        <line x1="9" y1="9" x2="15" y2="15" />
                        <line x1="15" y1="9" x2="9" y2="15" />
                      </svg>
                    </div>
                    <p className="empty-state-text">No conversations match your filters.</p>
                  </div>
                </div>
              ) : (
                filtered.map((item) => (
                  <HistoryItem key={item.id} item={item} />
                ))
              )}
            </div>
          </Card>
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.HistoryPage = HistoryPage;
})();
