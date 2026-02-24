// Search feature
import { createItem, setStatus, setDetail } from './helpers.js';

export function initSearch({ searchFormId, searchQueryId, searchResultsId }) {
  const searchForm = document.getElementById(searchFormId);
  const searchQuery = document.getElementById(searchQueryId);
  const searchResults = document.getElementById(searchResultsId);

  if (!searchForm || !searchQuery || !searchResults) return;

  searchForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    searchResults.innerHTML = '';
    const item = createItem(`Search: ${searchQuery.value}`);
    setStatus(item, 'Querying…');
    searchResults.prepend(item);

    try {
      const res = await fetch('/api/chat/search', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: searchQuery.value, top_k: 5 }),
      });
      const data = await res.json();
      setStatus(item, data.success ? 'Done' : 'Error', !!data.success);
      setDetail(item, data.success ? `${data.total_results} results` : data.detail || 'Error', !data.success);

      if (data.results?.length) {
        data.results.slice(0, 5).forEach((r) => {
          const el = document.createElement('div');
          el.className = 'meta';
          el.textContent = `• ${r.text?.slice(0, 120) || ''}${(r.text || '').length > 120 ? '…' : ''}  (score: ${r.score?.toFixed?.(3) ?? r.score})`;
          item.appendChild(el);
        });
      }
    } catch (err) {
      setStatus(item, 'Error');
      setDetail(item, String(err), true);
    }
  });
}
