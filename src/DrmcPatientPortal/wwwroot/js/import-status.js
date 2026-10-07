(() => {
    "use strict";
    const url = document.currentScript.dataset.statusUrl;
    const status = document.getElementById("import-status");
    const counts = document.getElementById("import-counts");
    const cancel = document.getElementById("import-cancel");
    const message = document.getElementById("import-poll-message");
    const retry = document.getElementById("import-poll-retry");
    const phase = value => ["ValidationQueued", "Validating"].includes(value) ? "validation"
        : ["ApprovalQueued", "Approving"].includes(value) ? "approval"
        : ["Queued", "Running"].includes(value) ? "execution" : "finished";
    const initialPhase = phase(status.dataset.initialStatus);
    let pending = true;
    let stopped = false;
    let reloaded = false;
    let failures = 0;
    let timer;
    let request;

    function schedule(delay = 3000) {
        clearTimeout(timer);
        if (pending && !stopped && !document.hidden && !request) timer = setTimeout(poll, delay);
    }

    function failed() {
        failures++;
        retry.hidden = false;
        if (failures >= 4) {
            stopped = true;
            message.textContent = "Status updates paused after repeated connection failures. Retry or refresh this page.";
        } else message.textContent = "Status is temporarily unavailable. Retrying shortly.";
    }

    async function poll() {
        if (document.hidden || !pending || stopped || request) return;
        const controller = new AbortController();
        request = controller;
        let timedOut = false;
        const timeout = setTimeout(() => { timedOut = true; controller.abort(); }, 10000);
        try {
            const response = await fetch(url, { credentials: "same-origin", cache: "no-store", signal: controller.signal });
            if (response.redirected || response.status === 401 || response.status === 403) {
                stopped = true;
                retry.hidden = true;
                message.textContent = "Status updates stopped. Sign in again or check access, then refresh this page.";
                return;
            }
            if (!response.ok) {
                if (response.status >= 500 || response.status === 408 || response.status === 429) failed();
                else {
                    stopped = true;
                    retry.hidden = false;
                    message.textContent = "Status updates are unavailable. Retry or refresh this page.";
                }
                return;
            }
            const batch = await response.json();
            if (document.hidden || controller.signal.aborted) return;
            if (typeof batch.status !== "string" || typeof batch.statusLabel !== "string"
                || !/^admin-chip admin-chip-(success|warning|danger|neutral|info)$/.test(batch.statusCss)
                || typeof batch.pending !== "boolean" || typeof batch.canCancel !== "boolean"
                || typeof batch.rowVersion !== "string" || !Number.isInteger(batch.rowCount) || !Number.isInteger(batch.createdCount)) throw new Error("Invalid status response");
            status.textContent = batch.statusLabel;
            status.className = batch.statusCss;
            counts.textContent = batch.rowCount + " / " + batch.createdCount;
            if (cancel) {
                cancel.querySelector("button").disabled = !batch.canCancel;
                cancel.querySelector("input[name=rowVersion]").value = batch.rowVersion;
            }
            failures = 0;
            retry.hidden = true;
            message.textContent = "Status updates every three seconds.";
            pending = batch.pending;
            if (!reloaded && phase(batch.status) !== initialPhase) {
                reloaded = true;
                pending = false;
                window.location.reload();
            }
        } catch (error) {
            if (!document.hidden && (error.name !== "AbortError" || timedOut)) failed();
        } finally {
            clearTimeout(timeout);
            request = null;
            schedule(Math.min(24000, 3000 * (2 ** failures)));
        }
    }

    retry.addEventListener("click", () => {
        if (request || reloaded) return;
        failures = 0;
        stopped = false;
        retry.hidden = true;
        message.textContent = "Checking batch status…";
        clearTimeout(timer);
        poll();
    });
    document.addEventListener("visibilitychange", () => {
        if (document.hidden) { clearTimeout(timer); if (request) request.abort(); }
        else schedule();
    });
    schedule();
})();
