(() => {
    "use strict";
    const form = document.getElementById("private-lab-upload");
    if (!form) return;
    const add = document.getElementById("private-add-row");
    add?.addEventListener("click", () => {
        const next = [...form.querySelectorAll(".private-upload-row")].find(row => row.hidden);
        if (next) { next.hidden = false; next.querySelector("select")?.focus(); }
        add.disabled = ![...form.querySelectorAll(".private-upload-row")].some(row => row.hidden);
    });
    form.querySelectorAll(".private-pending-select").forEach(select => {
        const update = () => {
            const row = select.closest("fieldset");
            const option = select.selectedOptions[0];
            row.querySelector(".private-request-version").value = option?.dataset.version || "";
            const description = row.querySelector(".private-request-description");
            description.readOnly = !!select.value;
            if (select.value) description.value = option.dataset.description || "";
        };
        select.addEventListener("change", update);
        update();
    });
})();
