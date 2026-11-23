// One SDK client per tab. The circuit gets only the access token needed by the profile API.
window.cfcAuth = (() => {
    let auth;
    let subscription;
    let listener;
    let connection;
    let storageKey;
    let operations = 0;
    let sdkLoad;
    let recoveryAuth;
    let recoveryLink = takeRecoveryLink();

    // FastAPI's /auth/v1/recover request uses Supabase's implicit recovery redirect.
    // Consume its fragment before Blazor starts. Only status codes cross the circuit.
    function takeRecoveryLink() {
        if (!window.location?.hash) return null;
        const { pathname, search, hash } = window.location;
        const params = new URLSearchParams(hash.slice(1));
        if (pathname !== "/reset-password") {
            // The configured frontend base can land at / or its /login redirect.
            if ((pathname === "/" || pathname === "/login") &&
                (params.get("type") === "recovery" || params.get("error_code") === "otp_expired")) {
                window.history.replaceState(window.history.state, "", pathname + search);
                window.location.replace("/reset-password" + hash);
            }
            return null;
        }
        window.history.replaceState(window.history.state, "", pathname + search);
        if (params.get("type") !== "recovery" || params.has("error")) return {};
        return { access_token: params.get("access_token"), refresh_token: params.get("refresh_token") };
    }

    function authFetch(input, init = {}) {
        const timeout = AbortSignal.timeout(30000);
        const signal = init.signal ? AbortSignal.any([init.signal, timeout]) : timeout;
        return fetch(input, { ...init, signal });
    }

    function loadSdk() {
        if (!sdkLoad) {
            sdkLoad = new Promise((resolve, reject) => {
                const script = document.createElement("script");
                script.src = "https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2.119.0/dist/umd/supabase.js";
                script.integrity = "sha384-uteMfOloLDGt042uDz+xHGF3ym63/WQ6rFq7murQRG8Ih7QrXGEGNtWd8688sb4O";
                script.crossOrigin = "anonymous";
                const timeout = setTimeout(fail, 15000);
                function fail() {
                    clearTimeout(timeout);
                    script.onload = script.onerror = null;
                    script.remove();
                    reject(new Error("Authentication library unavailable."));
                }
                script.onerror = fail;
                script.onload = () => {
                    if (!window.supabase?.createClient) {
                        fail();
                        return;
                    }
                    clearTimeout(timeout);
                    resolve(window.supabase);
                };
                document.head.appendChild(script);
            }).catch(error => {
                sdkLoad = undefined;
                throw error;
            });
        }
        return sdkLoad;
    }

    async function initialize(url, publicKey, receiver, connectionId) {
        const sdk = await loadSdk();
        subscription?.unsubscribe();
        if (!auth) {
            storageKey = "cfc-supabase:" + new URL(url).origin;
            // Supabase also uses the SDK storage key as a BroadcastChannel name. Keep that
            // channel unique per document, even when a browser duplicates a tab's sessionStorage.
            const clientKey = "cfc-auth:" + crypto.randomUUID();
            const tabKey = key => key.startsWith(clientKey)
                ? storageKey + key.slice(clientKey.length) : storageKey + ":" + key;
            const storage = {
                getItem: key => sessionStorage.getItem(tabKey(key)),
                setItem: (key, value) => sessionStorage.setItem(tabKey(key), value),
                removeItem: key => sessionStorage.removeItem(tabKey(key))
            };
            auth = sdk.createClient(url, publicKey, {
                global: { fetch: authFetch },
                auth: { storage, storageKey: clientKey, persistSession: true,
                    autoRefreshToken: true, detectSessionInUrl: false }
            }).auth;
        }
        listener = receiver;
        connection = connectionId;
        subscription = auth.onAuthStateChange((event, session) => {
            // SDK callbacks execute under its session lock. Read the current session after it releases.
            if (operations || event === "INITIAL_SESSION") return;
            setTimeout(() => {
                if (listener === receiver) {
                    receiver.invokeMethodAsync("BrowserSessionChanged", event, session?.user?.id ?? null).catch(() => {});
                }
            }, 0);
        }).data.subscription;
    }

    function classify(error) {
        if (error.code === "invalid_credentials" || error.code === "email_not_confirmed") return "credentials";
        if (error.name === "AuthSessionMissingError" || error.status === 401 || error.status === 403 || error.code === "refresh_token_not_found"
            || error.code === "refresh_token_already_used" || error.code === "session_not_found") return "expired";
        if (error.status === 429) return "rate_limit";
        return "unavailable";
    }

    async function read(operation) {
        operations++;
        try {
            const { data, error } = await operation();
            if (error) return { error: classify(error) };
            return { accessToken: data.session?.access_token ?? null };
        } catch {
            return { error: "unavailable" };
        } finally {
            operations--;
        }
    }

    async function checkSession(operation) {
        // The SDK can wait on storage locks or a token refresh while the form is disabled.
        let timer;
        const deadline = new Promise((_, reject) => { timer = setTimeout(reject, 30000); });
        try {
            return await read(() => Promise.race([operation(), deadline]));
        } finally {
            clearTimeout(timer);
        }
    }

    function signIn(emailInput, passwordInput) {
        const email = emailInput.value.trim();
        const password = passwordInput.value;
        passwordInput.value = "";
        return read(() => auth.signInWithPassword({ email, password }));
    }

    async function prepareRecovery(url, publicKey) {
        if (recoveryAuth) return true;
        const link = recoveryLink;
        if (!link?.access_token || !link.refresh_token) return false;
        const sdk = await loadSdk();
        const client = sdk.createClient(url, publicKey, {
            global: { fetch: authFetch },
            auth: { storageKey: "cfc-recovery:" + crypto.randomUUID(), persistSession: false,
                autoRefreshToken: false, detectSessionInUrl: false }
        }).auth;
        // Validate the link's unexpired access token independently of any existing tab session.
        const verified = await client.getUser(link.access_token);
        if (recoveryLink !== link) return false;
        if (verified.error || !verified.data?.user?.id) { discardRecovery(); return false; }
        const result = await client.refreshSession({ refresh_token: link.refresh_token });
        if (recoveryLink !== link) return false;
        recoveryLink = null;
        if (result.error || result.data?.session?.user?.id !== verified.data.user.id) return false;
        recoveryAuth = client;
        return true;
    }

    function discardRecovery() {
        recoveryLink = null;
        recoveryAuth = null;
    }

    async function updatePassword(passwordInput, confirmationInput) {
        const client = recoveryAuth;
        if (!client) return "invalid";
        const password = passwordInput.value;
        if (password.length < 6) return "short";
        if (password !== confirmationInput.value) return "mismatch";
        passwordInput.value = confirmationInput.value = "";
        const { error } = await client.updateUser({ password });
        if (error) return classify(error) === "expired" ? "invalid" : "rejected";
        discardRecovery();
        try { await client.signOut({ scope: "local" }); } catch { /* Password update already succeeded. */ }
        return "updated";
    }

    async function signOut() {
        operations++;
        try {
            // The pinned SDK clears local state first, then attempts remote revocation.
            const { error } = await auth.signOut({ scope: "local" });
            return !error;
        } finally {
            try {
                sessionStorage.removeItem(storageKey);
            } finally {
                operations--;
            }
        }
    }

    function disconnect(connectionId) {
        if (connection !== connectionId) return;
        subscription?.unsubscribe();
        subscription = null;
        listener = null;
    }

    return {
        initialize, signIn, signOut, disconnect,
        readEmail: input => input.value.trim(),
        hasRecoveryLink: () => Boolean(recoveryLink || recoveryAuth), prepareRecovery, updatePassword, discardRecovery,
        restore: () => checkSession(() => auth.getSession()),
        refresh: () => checkSession(() => auth.refreshSession())
    };
})();
