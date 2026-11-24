import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import vm from "node:vm";

const script = name => readFileSync(new URL("../../src/CFC.ChatAI.Web/wwwroot/js/" + name, import.meta.url), "utf8");
const chatScript = script("chat.js");
const preferenceScript = script("preferences.js");

function previewTab() {
    const urls = new Set();
    let id = 0;
    const URL = {
        createObjectURL() { const u = "blob:test/" + ++id; urls.add(u); return u; },
        revokeObjectURL(u) { assert.ok(urls.delete(u)); }
    };
    const window = {};
    vm.runInNewContext(chatScript, { window, URL });
    return { api: window.cfcChat, urls };
}
function sample(type, bytes) {
    return new File([Uint8Array.from(bytes)], "sample", { type });
}
const png = () => sample("image/png", [137, 80, 78, 71, 13, 10, 26, 10]);
test("browser creates removable object URLs and never returns file bytes", async () => {
    const { api, urls } = previewTab();
    const input = { files: [png(), sample("image/jpeg", [255, 216, 255])], value: "chosen" };
    const result = await api.selectImages(input, 4, 1024, 4096);
    assert.equal(input.value, "");
    assert.equal(result.error, null);
    assert.equal(urls.size, 2);
    for (const image of result.images) assert.deepEqual(Object.keys(image).sort(), ["name", "size", "url"]);
    api.releaseImages(result.images.map(i => i.url));
    assert.equal(urls.size, 0);
});
test("rejects empty, disguised, oversized and unsupported images before allocating URLs", async () => {
    const { api, urls } = previewTab();
    for (const file of [sample("image/png", []), sample("image/png", [60, 115, 118, 103]),
        sample("image/svg+xml", [60, 115, 118, 103]), sample("image/png", new Array(1000).fill(0))]) {
        const result = await api.selectImages({ files: [file] }, 4, 128, 2048);
        assert.ok(result.error);
        assert.equal(result.images.length, 0);
    }
    assert.equal(urls.size, 0);
});
test("selection honors remaining count and tab byte budget including multiple picker visits", async () => {
    for (const [slots, bytes] of [[1, 1024], [4, 8]]) {
        const { api, urls } = previewTab();
        const result = await api.selectImages({ files: [png(), png()] }, slots, 128, bytes);
        assert.equal(result.images.length, 1);
        assert.ok(result.error);
        assert.equal(urls.size, 1);
    }
});

function browser(storage = new Map(), blocked = false, allowAdmin = false) {
    let elementType, listener;
    const dataset = {};
    let panel;
    class Element {
        constructor() {
            this.selects = [["theme", "light", "dark"], ["text-size", "small", "medium", "large"],
                ["starting-page", "auto", "chat", "history", ...(allowAdmin ? ["admin"] : [])]]
                .map(([key, ...values]) => ({ dataset: { preference: key }, options: values.map(value => ({ value })) }));
            this.notice = { hidden: true };
            this.toggle = { setAttribute(key, value) { this[key] = value; } };
        }
        querySelectorAll() { return this.selects; }
        querySelector(selector) { return selector === "[data-theme-toggle]" ? this.toggle : this.notice; }
    }
    const localStorage = {
        getItem(key) { if (blocked) throw Error(); return storage.get(key) ?? null; },
        setItem(key, value) { if (blocked) throw Error(); storage.set(key, value); }
    };
    const window = { addEventListener(name, cb) { if (name === "storage") listener = cb; } };
    vm.runInNewContext(preferenceScript, {
        document: { documentElement: { dataset }, querySelectorAll: () => [panel] }, window, localStorage,
        HTMLElement: Element, customElements: { define(name, type) { elementType = type; } },
        MutationObserver: class { observe() {} disconnect() {} }
    });
    panel = new elementType();
    panel.connectedCallback();
    function choose(key, value) {
        const select = panel.selects.find(s => s.dataset.preference === key);
        select.value = value;
        panel.onchange({ target: select });
    }
    return { dataset, panel, choose, storageEvent: listener, startingPage: window.cfcPreferences.startingPage };
}
test("theme, text size and starting page survive refresh using the React storage keys", () => {
    const storage = new Map();
    const tab = browser(storage);
    tab.panel.onclick({ target: { closest: () => true } });
    tab.choose("text-size", "large");
    tab.choose("starting-page", "history");
    assert.equal(tab.panel.toggle["aria-pressed"], "true");
    const restored = browser(storage);
    assert.deepEqual(restored.dataset, { theme: "dark", textSize: "large" });
    assert.equal(restored.startingPage(), "history");
    restored.storageEvent({ key: "cfc-theme", newValue: "light" });
    assert.equal(restored.dataset.theme, "light");
});
test("blocked storage keeps usable in-memory preferences and explains their lifetime", () => {
    const tab = browser(new Map(), true);
    tab.choose("theme", "dark");
    tab.choose("text-size", "small");
    tab.choose("starting-page", "history");
    assert.deepEqual(tab.dataset, { theme: "dark", textSize: "small" });
    assert.equal(tab.startingPage(), "history");
    assert.equal(tab.panel.notice.hidden, false);
});
test("a saved Docs starting page falls back to the account default", () => {
    const tab = browser(new Map([["cfc-starting-page", "docs"]]));
    assert.equal(tab.startingPage(), "auto");
    assert.equal(tab.panel.selects[2].value, "auto");
});
test("unknown saved values fall back and a restricted starting page is absent from the member selector", () => {
    const storage = new Map([["cfc-theme", "other"], ["cfc-text-size", "999"], ["cfc-starting-page", "admin"]]);
    const tab = browser(storage);
    assert.deepEqual(tab.dataset, { theme: "light", textSize: "medium" });
    assert.equal(tab.panel.selects[2].value, "auto");
    assert.equal(browser(storage, false, true).panel.selects[2].value, "admin");
    tab.storageEvent({ key: "cfc-starting-page", newValue: "https://external.invalid" });
    assert.equal(tab.startingPage(), "auto");
});
