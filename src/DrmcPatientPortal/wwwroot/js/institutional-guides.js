// Stable MVC collection indexes preserve nested rows while staff add, remove or reorder them.
(() => {
    const editor = document.querySelector("[data-guide-editor]");
    if (!editor) return;
    const direct = (parent, selector) => Array.from(parent.children).find(child => child.matches(selector));
    const localeState = locale => {
        const enabled = locale.querySelector("[data-guide-override]")?.checked;
        const fields = locale.querySelector("[data-guide-locale-fields]");
        if (fields) fields.hidden = !enabled;
    };
    editor.querySelectorAll("[data-guide-locale]").forEach(localeState);
    editor.addEventListener("change", event => {
        if (event.target.matches("[data-guide-override]")) localeState(event.target.closest("[data-guide-locale]"));
    });
    editor.addEventListener("click", event => {
        const button = event.target.closest("button");
        if (!button || !editor.contains(button)) return;
        const repeater = button.closest("[data-guide-repeater]");
        if (!repeater) return;
        const items = direct(repeater, "[data-guide-items]");
        if (button.hasAttribute("data-guide-add")) {
            const source = direct(repeater, "template");
            const token = repeater.dataset.guideToken;
            if (!source || !items || !token) return;
            const index = "new" + crypto.randomUUID().replaceAll("-", "");
            const template = document.createElement("template");
            template.innerHTML = source.innerHTML.replaceAll(token, index);
            items.append(template.content.cloneNode(true));
            items.lastElementChild?.querySelector("input:not([type=hidden]), textarea")?.focus();
            return;
        }
        const row = button.closest("[data-guide-row]");
        if (!row || row.parentElement !== items) return;
        if (button.hasAttribute("data-guide-remove")) {
            const focusTarget = row.nextElementSibling || row.previousElementSibling;
            row.remove();
            (focusTarget?.querySelector("input:not([type=hidden]), textarea") || direct(repeater, "[data-guide-add]"))?.focus();
        } else if (button.hasAttribute("data-guide-up") && row.previousElementSibling) {
            items.insertBefore(row, row.previousElementSibling);
            button.focus();
        } else if (button.hasAttribute("data-guide-down") && row.nextElementSibling) {
            items.insertBefore(row.nextElementSibling, row);
            button.focus();
        }
    });
})();
