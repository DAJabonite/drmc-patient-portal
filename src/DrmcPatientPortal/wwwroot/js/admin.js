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
