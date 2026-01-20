// Developer / Docs page
(() => {
  const { Layout } = window.CFC.Layout;
  const { Card } = window.CFC.Primitives;
  const { useTheme } = window.CFC.ThemeContext;

  function DocsPage() {
    const { isDark } = useTheme();
    const iframeRef = React.useRef(null);

    React.useEffect(() => {
      const iframe = iframeRef.current;
      if (!iframe) return;

      const applyDarkMode = () => {
        try {
          const iframeDoc = iframe.contentDocument || iframe.contentWindow?.document;
          if (!iframeDoc) return;

          if (isDark) {
            iframeDoc.documentElement.classList.add('dark-mode');
          } else {
            iframeDoc.documentElement.classList.remove('dark-mode');
          }

          let styleEl = iframeDoc.getElementById('swagger-dark-mode-styles');
          if (!styleEl) {
            styleEl = iframeDoc.createElement('style');
            styleEl.id = 'swagger-dark-mode-styles';
            iframeDoc.head.appendChild(styleEl);
          }

          styleEl.textContent = `
            .dark-mode body { background: #1F2937 !important; color: #F9FAFB !important; }
            .dark-mode .swagger-ui { background: #1F2937 !important; color: #F9FAFB !important; }
            .dark-mode .swagger-ui .topbar { background: #111827 !important; border-bottom: 1px solid #374151 !important; }
            .dark-mode .swagger-ui .info { background: #1F2937 !important; color: #F9FAFB !important; }
            .dark-mode .swagger-ui .info .title { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .info .description,
            .dark-mode .swagger-ui .info p,
            .dark-mode .swagger-ui .info .base-url { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .scheme-container { background: #1F2937 !important; border-color: #374151 !important; }
            .dark-mode .swagger-ui .scheme-container label { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .opblock { background: #374151 !important; border-color: #4B5563 !important; }
            .dark-mode .swagger-ui .opblock.opblock-get { border-color: #4065AE !important; background: rgba(64, 101, 174, 0.1) !important; }
            .dark-mode .swagger-ui .opblock.opblock-post { border-color: #379D63 !important; background: rgba(55, 157, 99, 0.1) !important; }
            .dark-mode .swagger-ui .opblock.opblock-put { border-color: #D0A97C !important; background: rgba(208, 169, 124, 0.1) !important; }
            .dark-mode .swagger-ui .opblock.opblock-delete { border-color: #EF4444 !important; background: rgba(239, 68, 68, 0.1) !important; }
            .dark-mode .swagger-ui .opblock-tag { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .opblock-tag small { color: #9CA3AF !important; }
            .dark-mode .swagger-ui .opblock-summary { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .opblock-summary-path,
            .dark-mode .swagger-ui .opblock-summary-path__deprecated { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .opblock-summary-description { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .opblock-description-wrapper p,
            .dark-mode .swagger-ui .opblock-description-wrapper code { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .opblock-description-wrapper,
            .dark-mode .swagger-ui .opblock-description { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .parameter__name,
            .dark-mode .swagger-ui .parameter__type { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .parameter__in,
            .dark-mode .swagger-ui .parameter__extension,
            .dark-mode .swagger-ui .parameter__deprecated { color: #9CA3AF !important; }
            .dark-mode .swagger-ui .response-col_status { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .response-col_description { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .response-col_links { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .model-box { background: #374151 !important; color: #F9FAFB !important; }
            .dark-mode .swagger-ui .model-title { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .prop-name { color: #9CA3AF !important; }
            .dark-mode .swagger-ui .prop-type { color: #60A5FA !important; }
            .dark-mode .swagger-ui table thead tr th { background: #374151 !important; color: #F9FAFB !important; border-color: #4B5563 !important; }
            .dark-mode .swagger-ui table tbody tr td { background: #1F2937 !important; color: #D1D5DB !important; border-color: #374151 !important; }
            .dark-mode .swagger-ui .btn { background: #4065AE !important; color: #FFFFFF !important; border-color: #4065AE !important; }
            .dark-mode .swagger-ui .btn:hover { background: #4A73C4 !important; }
            .dark-mode .swagger-ui input[type="text"],
            .dark-mode .swagger-ui input[type="email"],
            .dark-mode .swagger-ui input[type="password"],
            .dark-mode .swagger-ui textarea,
            .dark-mode .swagger-ui select { background: #374151 !important; color: #F9FAFB !important; border-color: #4B5563 !important; }
            .dark-mode .swagger-ui .highlight-code { background: #111827 !important; }
            .dark-mode .swagger-ui code { background: #111827 !important; color: #D1D5DB !important; border-color: #374151 !important; }
            .dark-mode .swagger-ui a { color: #60A5FA !important; }
            .dark-mode .swagger-ui a:hover { color: #93C5FD !important; }
            .dark-mode .swagger-ui .markdown p,
            .dark-mode .swagger-ui .markdown code,
            .dark-mode .swagger-ui .markdown pre { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .renderedMarkdown p { color: #D1D5DB !important; }
            .dark-mode .swagger-ui label { color: #D1D5DB !important; }
            .dark-mode .swagger-ui .parameter__name.required { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .parameter__name.required::after { color: #EF4444 !important; }
            .dark-mode .swagger-ui .models { background: #1F2937 !important; border-color: #374151 !important; }
            .dark-mode .swagger-ui .models-control { background: #374151 !important; border-color: #4B5563 !important; color: #F9FAFB !important; }
            .dark-mode .swagger-ui .models-control label { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .model-container { background: #374151 !important; border-color: #4B5563 !important; }
            .dark-mode .swagger-ui .model-box { background: #374151 !important; border-color: #4B5563 !important; }
            .dark-mode .swagger-ui .model-toggle { background: #374151 !important; color: #F9FAFB !important; }
            .dark-mode .swagger-ui .model-toggle:hover { background: #4B5563 !important; }
            .dark-mode .swagger-ui .model-jump-to-path { color: #60A5FA !important; }
            .dark-mode .swagger-ui .model-jump-to-path:hover { color: #93C5FD !important; }
            .dark-mode .swagger-ui .opblock-summary-control { color: #F9FAFB !important; }
            .dark-mode .swagger-ui .opblock-summary-control path { fill: #F9FAFB !important; }
          `;
        } catch (err) {
          console.debug('Could not apply dark mode to iframe:', err);
        }
      };

      if (iframe.contentDocument?.readyState === 'complete') {
        applyDarkMode();
      } else {
        iframe.addEventListener('load', applyDarkMode);
      }

      applyDarkMode();

      return () => {
        iframe.removeEventListener('load', applyDarkMode);
      };
    }, [isDark]);

    return (
      <Layout>
        <div className="page docs-page">
          <div className="page-header-row">
            <div>
              <h1>Developer Workspace</h1>
              <p>
                Explore and test the HTTP API alongside a focused view
                of your documentation.
              </p>
            </div>
          </div>

          <div className="docs-grid">
            <Card className="docs-card">
              <h2>Interactive API Docs</h2>
              <p className="muted">
                This is the same OpenAPI/Swagger documentation exposed at <code>/docs</code>, wrapped in a scrollable surface.
              </p>
              <div className="docs-iframe-wrapper">
                <iframe ref={iframeRef} src="/docs" title="API docs" className="docs-iframe" />
              </div>
            </Card>

            <Card className="docs-card notes-card">
              <h2>Implementation notes</h2>
              <ul className="notes-list">
                <br />
                <li>Search and chat endpoints backed by your RAG pipeline.</li>
                <br />
                <li>Video ingestion with Whisper and Pinecone indexing.</li>
                <br />
                <li>Admin uploads immediately trigger ingestion workflows.</li>
                <br />
                <li>Some elements of the interactive API docs may not display correctly in dark mode because of Swagger. Toggling dark mode on and off can sometimes fix this.</li>
              </ul>
            </Card>
          </div>
        </div>
      </Layout>
    );
  }

  window.CFC = window.CFC || {};
  window.CFC.Pages = window.CFC.Pages || {};
  window.CFC.Pages.DocsPage = DocsPage;
})();
