// Progressive enhancement for explicitly opted-in GET browse forms.
(() => {
    const status = document.querySelector('[data-live-status]');
    if (!status || !window.fetch || !window.AbortController) return;
    let timer;
    let request;
    let revision = 0;
    const composing = new WeakSet();
    const main = () => document.getElementById('admin-main') ?? document.getElementById('main-content');
    const browseForm = (element) => element?.form?.matches('form[data-live-filter][method="get"]') ? element.form : null;
    const textInput = (element) => element instanceof HTMLInputElement && ['text', 'search'].includes(element.type);

    const announce = (message, error = false) => {
        status.textContent = status.dataset[message] ?? '';
        status.classList.toggle('visually-hidden', !error);
    };
    const invalidate = () => {
        revision++;
        clearTimeout(timer);
        request?.abort();
        request = null;
        main()?.removeAttribute('aria-busy');
        announce('');
    };
    const queryUrl = (form) => {
        const url = new URL(form.action, location.href);
        url.search = new URLSearchParams(new FormData(form)).toString();
        url.searchParams.delete('page');
        url.searchParams.delete('legacyPage');
        return url;
    };

    const refresh = async (form, expected) => {
        if (!form.isConnected || !form.checkValidity() || expected !== revision) return;
        const root = main();
        if (!root || !root.contains(form)) return;
        const url = queryUrl(form);
        const controller = new AbortController();
        request = controller;
        root.setAttribute('aria-busy', 'true');
        announce('loading');
        try {
            const response = await fetch(url, { signal: controller.signal, credentials: 'same-origin', cache: 'no-store' });
            if (expected !== revision) return;
            if (response.redirected || response.status === 401 || response.status === 403) {
                location.assign(response.redirected ? response.url : url.href);
                return;
            }
            if ((!response.ok && response.status !== 400) || !response.headers.get('content-type')?.includes('text/html')) throw new Error('List unavailable');
            const documentCopy = new DOMParser().parseFromString(await response.text(), 'text/html');
            if (expected !== revision) return;
            const replacement = documentCopy.getElementById(root.id);
            if (!replacement?.querySelector('form[data-live-filter]')) throw new Error('List unavailable');

            // Read focus/scroll at commit time, so a slow request cannot undo the user's later movement.
            const active = document.activeElement;
            const hadFocus = root.contains(active);
            const activeForm = browseForm(active);
            const formIndex = [...root.querySelectorAll('form[data-live-filter]')].indexOf(activeForm);
            const selection = textInput(active) ? [active.selectionStart, active.selectionEnd, active.selectionDirection] : null;
            const scroll = { left: window.scrollX, top: window.scrollY, behavior: 'instant' };
            const disclosures = [...root.querySelectorAll('.patient-report-filter-disclosure')].map(element => element.open);
            const panels = [...root.querySelectorAll('.encounter-date-panel, .lab-date-panel')].map(element => element.classList.contains('is-open'));
            replacement.querySelectorAll('script').forEach(script => script.remove());
            root.replaceWith(replacement);
            document.title = documentCopy.title || document.title;
            history.replaceState(history.state, '', url);
            document.dispatchEvent(new CustomEvent('drmc:list-updated', { detail: { root: replacement } }));
            replacement.querySelectorAll('.patient-report-filter-disclosure').forEach((element, index) => { element.open = disclosures[index] ?? element.open; });
            replacement.querySelectorAll('.encounter-date-panel, .lab-date-panel').forEach((element, index) => {
                if (panels[index] !== undefined) element.classList.toggle('is-open', panels[index]);
                document.querySelector(`[aria-controls="${element.id}"]`)?.setAttribute('aria-expanded', String(element.classList.contains('is-open')));
            });
            if (hadFocus) {
                const nextForm = replacement.querySelectorAll('form[data-live-filter]')[formIndex];
                const nextActive = (active.id ? document.getElementById(active.id) : null)
                    ?? (active.name ? nextForm?.elements.namedItem(active.name) : null);
                const target = nextActive instanceof HTMLElement ? nextActive : replacement;
                target.focus({ preventScroll: true });
                if (selection && textInput(target)) target.setSelectionRange(...selection);
            }
            window.scrollTo(scroll);
            announce(response.ok ? 'updated' : '');
        } catch (error) {
            if (expected === revision && error.name !== 'AbortError') announce('error', true);
        } finally {
            if (expected === revision) {
                main()?.removeAttribute('aria-busy');
                request = null;
            }
        }
    };
    const schedule = (form, delay = 0) => {
        invalidate();
        const expected = revision;
        timer = setTimeout(() => refresh(form, expected), delay);
    };

    document.addEventListener('compositionstart', event => {
        if (!browseForm(event.target)) return;
        composing.add(event.target);
        invalidate();
    });
    document.addEventListener('compositionend', event => {
        composing.delete(event.target);
        const form = browseForm(event.target);
        if (form?.hasAttribute('data-live-search') && textInput(event.target)) schedule(form, 450);
    });
    document.addEventListener('input', event => {
        const form = browseForm(event.target);
        if (!form) return;
        // Manual patient search still invalidates a pending render so it cannot erase unsent text.
        invalidate();
        if (!composing.has(event.target) && !event.isComposing && form.hasAttribute('data-live-search') && textInput(event.target)) schedule(form, 450);
    });
    document.addEventListener('change', event => {
        const form = browseForm(event.target);
        if (!form || composing.has(event.target) || textInput(event.target)) return;
        if (event.target.dataset.liveReveal === event.target.value) { invalidate(); return; }
        schedule(form);
    });
    document.addEventListener('submit', event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (!form.matches('form[data-live-filter][method="get"]')) { invalidate(); return; }
        event.preventDefault();
        schedule(form);
    });
    document.addEventListener('click', event => {
        const link = event.target.closest?.('a[href]');
        if (link && !event.defaultPrevented && !event.ctrlKey && !event.metaKey && !event.shiftKey && event.button === 0 && link.target !== '_blank') invalidate();
    });
    window.addEventListener('pagehide', invalidate);
    window.addEventListener('popstate', () => {
        invalidate();
        if (main()?.querySelector('form[data-live-filter]')) location.reload();
    });
    window.addEventListener('pageshow', event => {
        if (event.persisted && main()?.querySelector('form[data-live-filter]')) location.reload();
    });
})();
