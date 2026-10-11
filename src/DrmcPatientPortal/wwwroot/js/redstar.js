// Supply estimates are suggestions; only a staff-approved date creates an appointment.
document.querySelectorAll('[data-redstar-dispense]').forEach(form => {
    const dispensed = form.querySelector('[data-dispense-date]');
    const supply = form.querySelector('[data-days-supplied]');
    const next = form.querySelector('[data-next-refill]');
    const output = form.querySelector('[data-supply-suggestion]');
    const use = form.querySelector('[data-use-supply-date]');
    if (!dispensed || !supply || !next || !output) return;
    let suggestion = '';
    function update() {
        const days = Number(supply.value);
        const base = new Date(dispensed.value + 'T00:00:00Z');
        suggestion = '';
        if (dispensed.value && Number.isInteger(days) && days > 0 && days <= 366 && !Number.isNaN(base.getTime())) {
            base.setUTCDate(base.getUTCDate() + days);
            suggestion = base.toISOString().slice(0, 10);
            output.textContent = 'Estimated supply end: ' + suggestion +
                (next.value && next.value > suggestion ? '. The proposed visit is after the estimated supply end; review with the patient.' : '. Review before approving the next refill.');
        } else output.textContent = 'Enter a pickup date and days supplied to estimate the supply end.';
        if (use) use.hidden = !suggestion || form.dataset.continuing !== 'true';
    }
    [dispensed, supply, next].forEach(input => input.addEventListener('input', update));
    if (use) use.addEventListener('click', () => { next.value = suggestion; update(); });
    update();
});
