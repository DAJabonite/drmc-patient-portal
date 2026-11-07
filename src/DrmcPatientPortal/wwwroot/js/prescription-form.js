(() => {
    "use strict";
    const calculator = document.querySelector("[data-prescription-calculator]");
    if (!calculator || typeof BigInt !== "function") return;
    const field = (name) => calculator.querySelector(`[name="${name}"]`);
    const quantity = field("PrescribedQuantity");
    const dispensed = field("DispensedQuantity");
    const quantityOutput = calculator.querySelector("[data-calculated-quantity]");
    const refillOutput = calculator.querySelector("[data-calculated-refill]");
    const scale = 10000n;
    const maximum = 999999999999999999n;
    let dispensedEdited = dispensed.value !== "";
    let calculatedQuantity = field("FrequencyMode").value !== calculator.dataset.prnMode && quantity.value !== "";
    const decimal = (value) => {
        if (!/^\d+(?:\.\d{1,4})?$/.test(value)) return null;
        const [whole, fraction = ""] = value.split(".");
        const result = BigInt(whole) * scale + BigInt(fraction.padEnd(4, "0"));
        return result > 0n && result <= maximum ? result : null;
    };
    const format = (value) => `${value / scale}.${(value % scale).toString().padStart(4, "0")}`.replace(/\.?0+$/, "");
    const date = (value) => {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
        const [year, month, day] = value.split("-").map(Number);
        if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31) return null;
        const result = new Date(0);
        result.setUTCFullYear(year, month - 1, day);
        result.setUTCHours(0, 0, 0, 0);
        return result.getUTCMonth() === month - 1 ? result : null;
    };
    const calendarDays = () => {
        const raw = field("DurationCount").value;
        if (!/^\d+$/.test(raw)) return null;
        const count = Number(raw);
        if (!Number.isSafeInteger(count) || count < 1 || count > 2147483647) return null;
        if (field("DurationUnit").value !== calculator.dataset.daysUnit && field("DurationUnit").value !== calculator.dataset.monthsUnit) return null;
        const start = date(field("PrescribedAt").value.slice(0, 10));
        if (!start) return null;
        const end = new Date(start);
        if (field("DurationUnit").value === calculator.dataset.monthsUnit) {
            if (count > 120000) return null;
            const finalMonth = start.getUTCMonth() + count;
            const monthEnd = new Date(start);
            monthEnd.setUTCFullYear(start.getUTCFullYear(), finalMonth + 1, 0);
            end.setUTCFullYear(start.getUTCFullYear(), finalMonth, Math.min(start.getUTCDate(), monthEnd.getUTCDate()));
        } else end.setUTCDate(start.getUTCDate() + count);
        if (!Number.isFinite(end.getTime()) || end.getUTCFullYear() > 9999) return null;
        return BigInt(Math.round((end - start) / 86400000));
    };
    const update = () => {
        const mode = field("FrequencyMode").value;
        const prn = mode === calculator.dataset.prnMode;
        const served = field("FulfillmentStatus").value === calculator.dataset.servedStatus;
        if (prn && calculatedQuantity) {
            quantity.value = "";
            if (!dispensedEdited) dispensed.value = "";
            calculatedQuantity = false;
        }
        calculator.querySelector("[data-frequency-value]").hidden = prn;
        field("FrequencyValue").disabled = prn;
        quantity.readOnly = !prn;
        calculator.querySelectorAll("[data-served-field]").forEach((container) => {
            container.hidden = !served;
            container.querySelector("input").disabled = !served;
        });
        quantityOutput.textContent = calculator.dataset.wait;
        refillOutput.textContent = served ? calculator.dataset.wait : calculator.dataset.notServed;
        const dose = decimal(field("DosePerIntake").value);
        const days = calendarDays();
        if (!dose || !days || !field("DoseUnit").value.trim()) return;
        if (prn) {
            const prescribed = decimal(quantity.value);
            quantityOutput.textContent = prescribed ? `${format(prescribed)} ${field("DoseUnit").value}` : calculator.dataset.prn;
            refillOutput.textContent = served ? calculator.dataset.prn : calculator.dataset.notServed;
            if (served && prescribed && !dispensedEdited) dispensed.value = format(prescribed);
            return;
        }
        const frequency = decimal(field("FrequencyValue").value);
        if (!frequency || (mode !== calculator.dataset.timesMode && mode !== calculator.dataset.hoursMode)) return;
        if (mode === calculator.dataset.timesMode && (frequency % scale !== 0n || frequency > 2147483647n * scale)) return;
        const numerator = mode === calculator.dataset.timesMode ? frequency * dose : 24n * dose;
        const denominator = mode === calculator.dataset.timesMode ? scale * scale : frequency;
        const total = (numerator * days * scale + denominator - 1n) / denominator;
        if (total <= 0n || total > maximum) return;
        quantity.value = format(total);
        calculatedQuantity = true;
        quantityOutput.textContent = `${format(total)} ${field("DoseUnit").value}`;
        if (served && !dispensedEdited) dispensed.value = format(total);
        const actual = decimal(dispensed.value);
        const servedDate = date(field("ServedDate").value);
        if (!served || !actual || actual > total || !servedDate || field("ServedDate").value > calculator.dataset.today || field("ServedDate").value < field("PrescribedAt").value.slice(0, 10)) return;
        const supplyDays = actual * denominator / (scale * numerator);
        if (supplyDays > 2147483647n) return;
        servedDate.setUTCDate(servedDate.getUTCDate() + Number(supplyDays));
        if (!Number.isFinite(servedDate.getTime()) || servedDate.getUTCFullYear() > 9999) return;
        refillOutput.textContent = new Intl.DateTimeFormat(document.documentElement.lang || "en", { dateStyle: "long", timeZone: "UTC" }).format(servedDate);
    };
    dispensed.addEventListener("input", () => { dispensedEdited = true; });
    calculator.addEventListener("input", update);
    calculator.addEventListener("change", update);
    update();
})();
