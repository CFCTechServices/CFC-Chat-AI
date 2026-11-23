import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { setTimeout as wait } from "node:timers/promises";
import vm from "node:vm";

const bridge = readFileSync(new URL("../../src/CFC.ChatAI.Web/wwwroot/js/supabase-auth.js", import.meta.url), "utf8");

function createTab(storage = new Map(), loadScript = script => script.onload(), timers = { setTimeout, clearTimeout },
    browser = {}) {
    const events = [];
    const calls = [];
    let handler, options;
    const sessionStorage = {
        getItem: key => storage.get(key) ?? null,
        setItem: (key, value) => storage.set(key, value),
        removeItem: key => storage.delete(key)
    };
    const sdk = {
        onAuthStateChange(callback) {
            handler = callback;
            return { data: { subscription: { unsubscribe() { handler = null; } } } };
        },
        getSession() {
            const value = options.storage.getItem(options.storageKey);
            return { data: { session: value ? { access_token: value, refresh_token: randomUUID() } : null } };
        },
        async signInWithPassword(input) {
            calls.push(input);
            options.storage.setItem(options.storageKey, randomUUID());
            handler("SIGNED_IN", { user: { id: "person" } });
            return sdk.getSession();
        },
        async refreshSession() {
            options.storage.setItem(options.storageKey, randomUUID());
            return sdk.getSession();
        },
        async signOut(input) {
            calls.push(input);
            handler("SIGNED_OUT", null);
            return { error: { status: 503 } };
        }
    };
    let sdkFetch;
    const window = { ...browser.window, supabase: { createClient(url, key, config) {
        options = config.auth;
        sdkFetch = config.global.fetch;
        return { auth: sdk };
    } } };
    const document = {
        createElement: () => ({ remove() {} }),
        head: { appendChild: loadScript }
    };
    vm.runInNewContext(bridge, { window, document, sessionStorage, URL, URLSearchParams, AbortSignal,
        crypto: { randomUUID }, ...timers, ...browser.globals });
    const api = window.cfcAuth;
    const receiver = { async invokeMethodAsync(...args) { events.push(args); } };
    const initialize = () => api.initialize("https://example.test", "sb_publishable_" + randomUUID(), receiver, "current");
    return { api, storage, events, calls, initialize, get options() { return options; },
        get sdkFetch() { return sdkFetch; }, sdk, emit: (...args) => handler?.(...args) };
}

async function startTab(storage) {
    const tab = createTab(storage);
    await tab.initialize();
    return tab;
}

test("a failed SDK download can be retried without reloading the page", async () => {
    const scripts = [];
    const tab = createTab(new Map(), script => {
        scripts.push(script);
        if (scripts.length === 1) script.onerror();
        else script.onload();
    });
    assert.equal(scripts.length, 0);
    await assert.rejects(tab.initialize(), /library unavailable/);
    await tab.initialize();
    assert.equal(scripts.length, 2);
    assert.match(scripts[1].integrity, /^sha384-/);
    assert.equal(scripts[1].crossOrigin, "anonymous");
    assert.equal((await tab.api.restore()).accessToken, null);
});

test("initialization times out when the script never signals load or error and a retry starts a fresh load", async () => {
    let expire;
    const scripts = [];
    const timers = {
        setTimeout(callback, delay) {
            assert.ok(delay > 0 && delay <= 30000);
            expire = callback;
            return 1;
        },
        clearTimeout() { expire = undefined; }
    };
    const tab = createTab(new Map(), script => scripts.push(script), timers);
    const first = tab.initialize();
    assert.equal(typeof expire, "function");
    expire();
    await assert.rejects(first);

    const retry = tab.initialize();
    assert.equal(scripts.length, 2);
    scripts[1].onload();
    await retry;
    assert.equal(expire, undefined);
    assert.equal((await tab.api.restore()).accessToken, null);
});

test("submit reads autofilled fields without input events and only returns the access token", async () => {
    const tab = await startTab();
    const emailInput = { value: "  person@example.test  " };
    const passwordInput = { value: randomUUID() };
    const entered = passwordInput.value;
    const result = await tab.api.signIn(emailInput, passwordInput);
    assert.equal(tab.calls[0].email, "person@example.test");
    assert.equal(tab.calls[0].password, entered);
    assert.equal(passwordInput.value, "");
    assert.deepEqual(Object.keys(result), ["accessToken"]);
    assert.ok(result.accessToken);
    const restored = await startTab(tab.storage);
    const fresh = await startTab();
    assert.equal((await restored.api.restore()).accessToken, result.accessToken);
    assert.equal((await fresh.api.restore()).accessToken, null);
    assert.notEqual((await tab.api.refresh()).accessToken, result.accessToken);
    assert.equal(tab.options.persistSession, true);
    assert.equal(tab.options.autoRefreshToken, true);
});

test("a stalled session check releases the form and a later restore reads the current session", async () => {
    let timeout;
    const tab = createTab(new Map(), undefined, {
        setTimeout(callback) { timeout = callback; return 1; },
        clearTimeout() { timeout = undefined; }
    });
    await tab.initialize();
    for (const [action, method] of [["restore", "getSession"], ["refresh", "refreshSession"]]) {
        const original = tab.sdk[method];
        let finish;
        tab.sdk[method] = () => new Promise(resolve => { finish = resolve; });
        const check = tab.api[action]();
        assert.equal(typeof timeout, "function");
        timeout();
        assert.equal((await check).error, "unavailable");
        assert.equal(timeout, undefined);
        tab.sdk[method] = original;
        assert.equal((await tab.api.restore()).accessToken, null);
        finish({ data: { session: null } });
    }
});

test("SDK events notify the circuit outside the lock and cleanly unsubscribe", async () => {
    const tab = await startTab();
    tab.emit("INITIAL_SESSION", null);
    tab.api.disconnect("older-circuit");
    tab.emit("TOKEN_REFRESHED", { user: { id: "person" }, access_token: randomUUID() });
    assert.equal(tab.events.length, 0);
    await wait(10);
    assert.deepEqual(tab.events, [["BrowserSessionChanged", "TOKEN_REFRESHED", "person"]]);
    tab.api.disconnect("current");
    tab.emit("SIGNED_OUT", null);
    await wait(10);
    assert.equal(tab.events.length, 1);
});

test("duplicated tabs use separate SDK event channels and independent subsequent sessions", async () => {
    const first = await startTab();
    const signedIn = await first.api.signIn({ value: "person@example.test" }, { value: randomUUID() });
    const duplicate = await startTab(new Map(first.storage));
    assert.notEqual(first.options.storageKey, duplicate.options.storageKey);
    assert.equal((await duplicate.api.restore()).accessToken, signedIn.accessToken);
    await duplicate.api.signOut();
    assert.equal((await first.api.restore()).accessToken, signedIn.accessToken);
});

test("local sign-out removes only this session even when remote revocation fails", async () => {
    const tab = await startTab();
    tab.storage.set("unrelated", "keep");
    await tab.api.signIn({ value: "person@example.test" }, { value: randomUUID() });
    assert.equal(await tab.api.signOut(), false);
    assert.equal(tab.calls.at(-1).scope, "local");
    assert.equal((await tab.api.restore()).accessToken, null);
    assert.equal(tab.storage.get("unrelated"), "keep");
});

test("SDK failures return safe error codes", async () => {
    const tab = await startTab();
    tab.sdk.signInWithPassword = async () => ({ error: { code: "invalid_credentials" } });
    assert.equal((await tab.api.signIn({ value: "person@example.test" }, { value: randomUUID() })).error, "credentials");
    tab.sdk.getSession = async () => { throw new Error("Network unavailable"); };
    assert.equal((await tab.api.restore()).error, "unavailable");
});

test("the SDK transport aborts stalled requests and honors caller cancellation", async () => {
    const deadline = new AbortController();
    const tab = createTab(undefined, undefined, undefined, { globals: {
        AbortSignal: { timeout(ms) { assert.equal(ms, 30000); return deadline.signal; }, any: AbortSignal.any },
        fetch(_input, { signal }) {
            return new Promise((_, reject) => signal.addEventListener("abort", () => reject(signal.reason)));
        }
    } });
    await tab.initialize();
    const caller = new AbortController();
    const first = tab.sdkFetch("https://example.test", { signal: caller.signal });
    caller.abort();
    await assert.rejects(first, { name: "AbortError" });
    const second = tab.sdkFetch("https://example.test");
    deadline.abort();
    await assert.rejects(second, { name: "AbortError" });
});

function openResetLink(fragment) {
    const location = new URL("https://example.test/reset-password" + fragment);
    const browser = { window: { location, history: {
        replaceState(_state, _title, url) { location.href = new URL(url, location).href; }
    } } };
    const tab = createTab(undefined, undefined, undefined, browser);
    assert.equal(location.hash, "");
    return tab;
}

test("root or login recovery redirects stay on the same origin and scrub the original fragment", () => {
    for (const pathname of ["/", "/login"]) {
        const hash = "#type=recovery&access_token=" + randomUUID();
        let redirected;
        const location = { pathname, hash, search: "", replace(url) { redirected = url; } };
        const history = { replaceState(_state, _title, path) { assert.equal(path, pathname); location.hash = ""; } };
        createTab(undefined, undefined, undefined, { window: { location, history } });
        assert.equal(location.hash, "");
        assert.equal(redirected, "/reset-password" + hash);
    }
});

test("password recovery verifies both tokens for the same account and uses a separate in-memory session", async () => {
    const access = randomUUID(), refresh = randomUUID();
    const tab = openResetLink(`#type=recovery&access_token=${access}&refresh_token=${refresh}`);
    assert.equal(tab.api.hasRecoveryLink(), true);
    tab.sdk.getUser = async token => {
        assert.equal(token, access);
        return { data: { user: { id: "recovery-user" } } };
    };
    tab.sdk.refreshSession = async input => {
        assert.equal(input.refresh_token, refresh);
        return { data: { session: { user: { id: "recovery-user" } } } };
    };
    assert.equal(await tab.api.prepareRecovery("https://example.test", randomUUID()), true);
    assert.equal(tab.options.persistSession, false);
    assert.equal(tab.options.autoRefreshToken, false);
    const input = { value: randomUUID() }, confirm = { value: input.value };
    const entered = input.value;
    tab.sdk.updateUser = async ({ password }) => { assert.equal(password, entered); return {}; };
    tab.sdk.signOut = async ({ scope }) => { assert.equal(scope, "local"); return {}; };
    assert.equal(await tab.api.updatePassword(input, { value: "" }), "mismatch");
    assert.equal(await tab.api.updatePassword(input, confirm), "updated");
    assert.equal(input.value, "");
    assert.equal(tab.api.hasRecoveryLink(), false);
    assert.equal(await tab.api.updatePassword(input, confirm), "invalid");
});

test("ordinary, expired, and mismatched sessions cannot update a password through recovery", async () => {
    for (const fragment of ["", "#type=signup", "#error=access_denied", "#type=recovery"]) {
        const tab = openResetLink(fragment);
        assert.equal(await tab.api.prepareRecovery("https://example.test", randomUUID()), false);
        assert.equal(await tab.api.updatePassword({}, {}), "invalid");
    }
    for (const expired of [true, false]) {
        const tab = openResetLink(`#type=recovery&access_token=${randomUUID()}&refresh_token=${randomUUID()}`);
        tab.sdk.getUser = async () => expired ? { error: {} } : { data: { user: { id: "a" } } };
        tab.sdk.refreshSession = async () => ({ data: { session: { user: { id: "b" } } } });
        assert.equal(await tab.api.prepareRecovery("https://example.test", randomUUID()), false);
        assert.equal(await tab.api.updatePassword({}, {}), "invalid");
    }
});
