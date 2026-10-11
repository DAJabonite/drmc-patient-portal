(() => {
    const form = document.querySelector('[data-usage-filter]');
    if (!form) return;
    const mode = form.querySelector('[data-usage-mode]');
    const update = () => {
        form.querySelectorAll('[data-usage-period]').forEach(group => {
            const active = group.dataset.usagePeriod === mode.value;
            group.hidden = !active;
            group.querySelectorAll('input').forEach(input => { input.disabled = !active; });
        });
    };
    mode.addEventListener('change', update);
    update();
})();
