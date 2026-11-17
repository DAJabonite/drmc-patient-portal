(() => {
    if (!window.fetch) return;
    let queue = Promise.resolve();
    const pending = new Set();
    const formsFor = id => [...document.querySelectorAll('form[data-redstar-read]')].filter(form => form.dataset.redstarRead === id);

    document.addEventListener('submit', event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.matches('[data-redstar-read]')) return;
        event.preventDefault();
        const id = form.dataset.redstarRead;
        if (pending.has(id)) return;
        pending.add(id);
        const status = form.closest('[data-notification-surface]')?.querySelector('[data-notification-status]');
        if (status) {
            status.textContent = '';
            delete status.dataset.notificationErrorId;
        }
        formsFor(id).forEach(copy => copy.querySelectorAll('button').forEach(button => { button.disabled = true; }));
        const fields = new FormData(form);
        const action = form.action;
        // One queue for the schedule and both bell copies prevents stale unread counts.
        queue = queue.then(async () => {
            try {
                const response = await fetch(action, { method: 'POST', body: fields, credentials: 'same-origin',
                    headers: { Accept: 'application/json' }, cache: 'no-store' });
                if (response.redirected || response.status === 401 || response.status === 403) { location.reload(); return; }
                if (!response.ok) throw new Error('Read unavailable');
                const result = await response.json();
                if (!Number.isInteger(result.unreadCount) || result.unreadCount < 0) throw new Error('Read unavailable');
                document.querySelectorAll('[data-notification-count]').forEach(badge => {
                    badge.textContent = String(result.unreadCount);
                    badge.hidden = result.unreadCount === 0;
                });
                document.querySelectorAll('[data-notification-status]').forEach(message => {
                    if (message.dataset.notificationErrorId !== id) return;
                    message.textContent = '';
                    delete message.dataset.notificationErrorId;
                });
                const moveFocus = form.contains(document.activeElement);
                const nextFocus = form.closest('.redstar-notifications')?.querySelector('summary') ?? document.getElementById('reminders');
                document.querySelectorAll('[data-redstar-notification]').forEach(row => {
                    if (row.dataset.redstarNotification !== id) return;
                    row.querySelectorAll('[data-notification-unread]').forEach(element => { element.hidden = true; });
                    row.querySelectorAll('[data-notification-read]').forEach(element => { element.hidden = false; });
                });
                formsFor(id).forEach(copy => copy.remove());
                if (moveFocus && nextFocus) {
                    if (!nextFocus.hasAttribute('tabindex') && nextFocus.tagName !== 'SUMMARY') nextFocus.tabIndex = -1;
                    nextFocus.focus({ preventScroll: true });
                }
            } catch {
                if (status) {
                    status.dataset.notificationErrorId = id;
                    status.textContent = status.dataset.error;
                }
            } finally {
                pending.delete(id);
                formsFor(id).forEach(copy => copy.querySelectorAll('button').forEach(button => { button.disabled = false; }));
            }
        });
    });
    document.addEventListener('click', event => {
        document.querySelectorAll('.redstar-notifications[open]').forEach(menu => {
            if (!menu.contains(event.target)) menu.open = false;
        });
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        document.querySelectorAll('.redstar-notifications[open]').forEach(menu => {
            if (menu.contains(document.activeElement)) {
                menu.open = false;
                menu.querySelector('summary').focus({ preventScroll: true });
                event.preventDefault();
            }
        });
    });
})();
