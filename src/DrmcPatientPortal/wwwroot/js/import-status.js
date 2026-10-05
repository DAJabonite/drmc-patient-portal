(() => {
    "use strict";
    const url = document.currentScript.dataset.statusUrl;
    const status = document.getElementById("import-status");
    const counts = document.getElementById("import-counts");
    const cancel = document.getElementById("import-cancel");
    let pending = true;
    let timer;
    let request;

    function schedule() {
        clearTimeout(timer);
        if (pending && !document.hidden) timer = setTimeout(poll, 3000);
    }

    async function poll() {
        if (document.hidden || !pending) return;
        request = new AbortController();
        try {
            const response = await fetch(url, { credentials: "same-origin", cache: "no-store", signal: request.signal });
            if (!response.ok || response.redirected) { pending = false; return; }
            const batch = await response.json();
            if (status.textContent !== batch.status) status.textContent = batch.status;
            counts.textContent = batch.rowCount + " / " + batch.createdCount;
            if (cancel) {
                cancel.querySelector("button").disabled = !batch.canCancel;
                cancel.querySelector("input[name=rowVersion]").value = batch.rowVersion;
            }
            pending = batch.pending;
        } catch (error) {
            if (error.name !== "AbortError") pending = false;
        } finally { request = null; schedule(); }
    }

    document.addEventListener("visibilitychange", () => {
        if (document.hidden) { clearTimeout(timer); if (request) request.abort(); }
        else schedule();
    });
    schedule();
})();
