(() => {
    const options = {
        "theme": ["light", "dark"],
        "text-size": ["medium", "small", "large"],
        "starting-page": ["auto", "chat", "history", "admin"]
    };
    const values = {};
    let persistent = true;
    function normalize(key, value) {
        return options[key].includes(value) ? value : options[key][0];
    }
    for (const key of Object.keys(options)) {
        let saved;
        try { saved = localStorage.getItem("cfc-" + key); } catch { persistent = false; }
        values[key] = normalize(key, saved);
    }
    function apply() {
        document.documentElement.dataset.theme = values.theme;
        document.documentElement.dataset.textSize = values["text-size"];
        document.querySelectorAll("cfc-preferences").forEach(panel => panel.sync());
    }
    function update(key, value) {
        values[key] = normalize(key, value);
        try { localStorage.setItem("cfc-" + key, values[key]); persistent = true; }
        catch { persistent = false; }
        apply();
    }
    // Initialize before first paint. Only these public display keys are read.
    document.documentElement.dataset.theme = values.theme;
    document.documentElement.dataset.textSize = values["text-size"];
    window.cfcPreferences = { startingPage: () => values["starting-page"] };

    customElements.define("cfc-preferences", class extends HTMLElement {
        connectedCallback() {
            this.onchange = e => {
                const key = e.target.dataset.preference;
                if (Object.hasOwn(options, key)) update(key, e.target.value);
            };
            this.onclick = e => {
                if (e.target.closest("[data-theme-toggle]")) update("theme", values.theme === "dark" ? "light" : "dark");
            };
            // Blazor may replace options following a verified role change.
            this.observer = new MutationObserver(() => this.sync());
            this.observer.observe(this, { childList: true, subtree: true });
            this.sync();
        }
        disconnectedCallback() { this.observer.disconnect(); }
        sync() {
            for (const select of this.querySelectorAll("select[data-preference]")) {
                const saved = values[select.dataset.preference];
                select.value = Array.from(select.options).some(option => option.value === saved) ? saved : "auto";
            }
            const toggle = this.querySelector("[data-theme-toggle]");
            if (toggle) toggle.setAttribute("aria-pressed", String(values.theme === "dark"));
            const notice = this.querySelector("[data-storage-notice]");
            if (notice) notice.hidden = persistent;
        }
    });
    window.addEventListener("storage", event => {
        if (event.key !== null && !Object.keys(options).some(key => event.key === "cfc-" + key)) return;
        for (const key of Object.keys(options)) {
            if (event.key === null || event.key === "cfc-" + key) values[key] = normalize(key, event.newValue);
        }
        apply();
    });
})();
