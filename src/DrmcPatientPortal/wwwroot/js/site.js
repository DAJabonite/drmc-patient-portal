// DRMC Patient Portal — client behaviors.
// Philippine Standard Time (UTC+8) clock.

(function () {
    "use strict";

    function pad(n) { return n < 10 ? "0" + n : "" + n; }

    function updateClock() {
        var el = document.getElementById("govphClock");
        if (!el) { return; }
        // Philippine Standard Time is fixed at UTC+8 (no DST).
        var now = new Date();
        var utc = now.getTime() + (now.getTimezoneOffset() * 60000);
        var pst = new Date(utc + (8 * 3600000));

        var months = ["January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"];
        var days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

        var hours = pst.getHours();
        var minutes = pad(pst.getMinutes());
        var seconds = pad(pst.getSeconds());
        var ampm = hours >= 12 ? "PM" : "AM";
        hours = hours % 12;
        hours = hours ? hours : 12; // 0 becomes 12

        var dateStr = days[pst.getDay()] + ", " + months[pst.getMonth()] + " " + pst.getDate() + ", " + pst.getFullYear();
        var timeStr = hours + ":" + minutes + ":" + seconds + " " + ampm;

        el.textContent = dateStr + ", " + timeStr;
    }

    if (document.getElementById("govphClock")) {
        updateClock();
        setInterval(updateClock, 1000);
    }
})();

// CSP-safe replacements for inline handlers (script-src 'self' forbids onchange/onsubmit attributes).
document.addEventListener("change", (event) => {
    const control = event.target;
    if (control instanceof HTMLElement && control.matches("[data-auto-submit]")) control.form?.submit();
});
document.addEventListener("submit", (event) => {
    const form = event.target;
    if (form instanceof HTMLFormElement && form.dataset.confirm && !window.confirm(form.dataset.confirm)) event.preventDefault();
});

function clearPdfFeedback(form) {
    form.querySelector("[data-pdf-password]").removeAttribute("aria-invalid");
    form.querySelector("[data-pdf-error]").textContent = "";
    const openLink = form.querySelector("[data-pdf-open-link]");
    openLink.hidden = true;
    openLink.removeAttribute("href");
}

document.addEventListener("click", (event) => {
    const opener = event.target instanceof Element ? event.target.closest("[data-pdf-modal]") : null;
    if (!(opener instanceof HTMLElement)) return;
    const modalId = opener.dataset.pdfModal;
    const modal = modalId ? document.getElementById(modalId) : null;
    if (!(modal instanceof HTMLElement) || !window.bootstrap?.Modal) return;
    const form = modal.querySelector("[data-pdf-form]");
    const password = modal.querySelector("[data-pdf-password]");
    const label = modal.querySelector("[data-pdf-password-label]");
    const intent = opener.dataset.pdfIntent === "download" ? "download" : "view";
    if (!(form instanceof HTMLFormElement) || !(password instanceof HTMLInputElement) || !(label instanceof HTMLElement)) return;
    const intentField = form.querySelector("[data-pdf-intent-field]");
    if (intentField instanceof HTMLInputElement) intentField.value = intent;
    const titleText = intent === "download" ? label.dataset.pdfTitleDownload : label.dataset.pdfTitleView;
    if (titleText) label.textContent = titleText;
    clearPdfFeedback(form);
    password.value = "";
    const instance = window.bootstrap.Modal.getOrCreateInstance(modal);
    modal.addEventListener("shown.bs.modal", () => password.focus(), { once: true });
    modal.addEventListener("hide.bs.modal", () => {
        password.value = "";
        clearPdfFeedback(form);
    }, { once: true });
    instance.show();
});

document.addEventListener("submit", async (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches("[data-pdf-form]")) return;
    event.preventDefault();
    const submit = form.querySelector('button[type="submit"]');
    if (submit.disabled) return;

    const modal = form.closest(".pdf-password-modal");
    const password = form.querySelector("[data-pdf-password]");
    const error = form.querySelector("[data-pdf-error]");
    const openLink = form.querySelector("[data-pdf-open-link]");
    const body = new URLSearchParams(new FormData(form));
    const request = new AbortController();
    const cancel = () => request.abort();
    let errorMessage = "";
    let invalidPassword = false;

    clearPdfFeedback(form);
    password.value = "";
    submit.disabled = true;
    modal.addEventListener("hide.bs.modal", cancel, { once: true });
    try {
        const response = await fetch(form.action, {
            method: "POST",
            headers: { Accept: "application/json" },
            body,
            redirect: "error",
            signal: request.signal
        });
        if (!response.ok) {
            invalidPassword = response.status === 422;
            if ((invalidPassword || response.status === 423) && response.headers.get("Content-Type")?.startsWith("application/json")) {
                const result = await response.json();
                errorMessage = result.message || form.dataset.pdfErrorDefault;
            } else {
                errorMessage = response.status === 404 ? form.dataset.pdfErrorUnavailable
                    : response.status === 429 ? form.dataset.pdfErrorRateLimit
                    : form.dataset.pdfErrorDefault;
            }
            return;
        }

        const result = await response.json();
        if (typeof result.url !== "string" || !result.url) throw new Error("Missing PDF link.");
        const url = new URL(result.url, window.location.href);
        if (url.origin !== window.location.origin) throw new Error("Invalid PDF link.");
        if (request.signal.aborted) return;

        const dialog = window.bootstrap.Modal.getInstance(modal);
        if (body.get("intent") === "download") {
            window.location.assign(url.href);
            dialog.hide();
            return;
        }

        const tab = window.open("about:blank", "_blank");
        if (tab) {
            tab.opener = null;
            tab.location.replace(url.href);
            dialog.hide();
        } else {
            openLink.href = url.href;
            openLink.hidden = false;
            openLink.onclick = () => window.setTimeout(() => dialog.hide(), 0);
            openLink.focus();
        }
    } catch {
        errorMessage = form.dataset.pdfErrorDefault;
    } finally {
        modal.removeEventListener("hide.bs.modal", cancel);
        submit.disabled = false;
        if (errorMessage && !request.signal.aborted) {
            error.textContent = errorMessage;
            if (invalidPassword) password.setAttribute("aria-invalid", "true");
            password.focus();
        }
    }
});
