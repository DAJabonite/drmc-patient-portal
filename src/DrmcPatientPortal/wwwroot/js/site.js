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

// Keep the server-rendered confirmations usable when JavaScript or Bootstrap is unavailable.
(() => {
    if (!window.bootstrap?.Modal) return;
    document.querySelectorAll("details[data-pdf-action]").forEach((action) => {
        const summary = action.querySelector("summary");
        const form = action.querySelector("form");
        const password = form?.querySelector('input[type="password"]');
        const title = form?.querySelector("[data-pdf-title]");
        const description = form?.querySelector("[data-pdf-description]");
        const help = form?.querySelector("[data-pdf-help]");
        const cancel = form?.querySelector("[data-pdf-cancel]");
        if (!summary || !form || !password || !title || !description || !help || !cancel) return;

        const reopen = action.open;
        const modal = document.createElement("div");
        modal.id = password.id + "-modal";
        modal.className = "modal fade pdf-password-modal";
        modal.tabIndex = -1;
        modal.setAttribute("aria-hidden", "true");
        modal.setAttribute("aria-labelledby", title.id);
        modal.setAttribute("aria-describedby", description.id + " " + help.id);
        const dialog = document.createElement("div");
        dialog.className = "modal-dialog modal-dialog-centered";
        const content = document.createElement("div");
        content.className = "modal-content";
        modal.append(dialog);
        dialog.append(content);
        content.append(form);
        form.classList.remove("patient-pdf-confirmation");
        form.querySelector("[data-pdf-body]").classList.add("modal-body");
        const footer = form.querySelector("[data-pdf-footer]");
        footer.classList.remove("mt-3");
        footer.classList.add("modal-footer");
        cancel.hidden = false;
        cancel.setAttribute("data-bs-dismiss", "modal");
        document.body.append(modal);

        const opener = document.createElement("button");
        opener.type = "button";
        opener.className = summary.className + " patient-pdf-trigger";
        opener.append(...summary.childNodes);
        opener.setAttribute("aria-haspopup", "dialog");
        opener.setAttribute("aria-controls", modal.id);
        action.replaceWith(opener);
        const instance = window.bootstrap.Modal.getOrCreateInstance(modal);
        opener.addEventListener("click", () => {
            password.value = "";
            instance.show(opener);
        });
        modal.addEventListener("shown.bs.modal", () => {
            (form.querySelector("[data-pdf-error]") ?? password).focus();
        });
        modal.addEventListener("hidden.bs.modal", () => {
            password.value = "";
            opener.focus();
        });
        form.addEventListener("formdata", () => {
            password.value = "";
            instance.hide();
        });
        if (reopen) instance.show(opener);
    });
})();
