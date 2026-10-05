// Shared date-range filter behaviour for record lists. Element IDs come from the script tag's data-* attributes.
(() => {
    const config = document.currentScript?.dataset ?? {};
    const byId = (key) => (config[key] ? document.getElementById(config[key]) : null);
    const filterForm = byId("form");
    const mobileSubmitSelect = byId("mobileSubmit");
    const rangeSelect = byId("range");
    const customRange = byId("customRange");
    const startDate = byId("start");
    const endDate = byId("end");
    const dateToggle = byId("toggle");
    const datePanel = byId("panel");
    const today = config.today ?? "";

    if (!rangeSelect || !customRange || !startDate || !endDate) return;

    const setDatePanelOpen = (open) => {
        datePanel?.classList.toggle("is-open", open);
        dateToggle?.setAttribute("aria-expanded", open ? "true" : "false");
    };

    const syncRangeVisibility = () => {
        const isCustom = rangeSelect.value === "custom";
        customRange.classList.toggle("d-none", !isCustom);
        startDate.required = isCustom;
        endDate.required = isCustom;
    };

    rangeSelect.addEventListener("change", syncRangeVisibility);
    startDate.addEventListener("change", () => {
        endDate.min = startDate.value;
        if (endDate.value && endDate.value < startDate.value) endDate.value = startDate.value;
    });
    endDate.addEventListener("change", () => {
        startDate.max = endDate.value || today;
    });
    dateToggle?.addEventListener("click", () => {
        setDatePanelOpen(dateToggle.getAttribute("aria-expanded") !== "true");
    });
    datePanel?.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && window.matchMedia("(max-width: 767.98px)").matches) {
            setDatePanelOpen(false);
            dateToggle?.focus();
        }
    });
    mobileSubmitSelect?.addEventListener("change", () => {
        if (window.matchMedia("(max-width: 767.98px)").matches) filterForm?.requestSubmit();
    });

    syncRangeVisibility();
})();
