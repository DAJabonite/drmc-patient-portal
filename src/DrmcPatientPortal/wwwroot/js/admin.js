// Admin console behaviour (CSP-safe: no inline handlers).
(() => {
    const key = "drmc-admin-density";
    const button = document.querySelector("[data-admin-density]");
    const apply = (compact) => {
        document.body.classList.toggle("admin-compact", compact);
        button?.setAttribute("aria-pressed", compact ? "true" : "false");
        button?.setAttribute("title", compact ? "Comfortable rows" : "Compact rows");
    };
    let compact = false;
    try { compact = localStorage.getItem(key) === "compact"; } catch { /* storage unavailable */ }
    apply(compact);
    button?.addEventListener("click", () => {
        compact = !compact;
        apply(compact);
        try { localStorage.setItem(key, compact ? "compact" : "comfortable"); } catch { /* ignore */ }
    });
})();

// Print buttons (registration code slip).
document.querySelectorAll("[data-admin-print]").forEach((button) => button.addEventListener("click", () => window.print()));

document.addEventListener("DOMContentLoaded", () => {
    const validator = window.jQuery?.validator;
    if (!validator) return;
    const originalStep = validator.methods.step;
    validator.methods.step = function (value, element, param) {
        if (element.type === "time" || element.type === "datetime-local") {
            return this.optional(element) || (!element.validity.badInput && !element.validity.stepMismatch);
        }
        return originalStep.call(this, value, element, param);
    };
});
