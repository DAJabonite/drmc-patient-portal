(() => {
    const initialized = new WeakSet();
    const initialize = () => {
        const form = document.querySelector('[data-usage-filter]');
        if (!form || initialized.has(form)) return;
        initialized.add(form);
        const mode = form.querySelector('[data-usage-mode]');
        const update = () => {
            form.querySelectorAll('[data-usage-period]').forEach(group => {
                const active = group.dataset.usagePeriod === mode.value;
                group.hidden = !active;
                group.querySelectorAll('input').forEach(input => {
                    input.disabled = !active;
                    input.required = active;
                });
            });
        };
        mode.addEventListener('change', update);
        update();
    };
    initialize();
    document.addEventListener('drmc:list-updated', initialize);
})();
