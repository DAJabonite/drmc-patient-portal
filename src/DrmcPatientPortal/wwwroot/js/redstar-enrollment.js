// Worksheet layouts change presentation only. Move existing controls so entered
// values, validation state and readonly patient identity fields stay intact.
(() => {
    document.querySelectorAll("[data-redstar-enrollment]").forEach((form) => {
        const selector = form.querySelector("[data-registry-worksheet]");
        const row = form.querySelector("[data-registry-fields]");
        const description = form.querySelector("[data-registry-description]");
        if (!selector || !row || !description) return;
        const fields = new Map([...row.children].map((field) => [field.dataset.registryField, field]));

        const apply = () => {
            const option = selector.selectedOptions[0];
            if (!option) return;
            const columns = JSON.parse(option.dataset.columns);
            columns.forEach((column) => {
                const field = fields.get(column.Field);
                if (!field) return;
                field.querySelector("[data-registry-header]").textContent = column.Header;
                row.append(field);
            });
            description.textContent = `Following the ${option.value} worksheet's supported column order.`;
            selector.title = option.value;
        };
        selector.addEventListener("change", apply);
        apply();
    });
})();
