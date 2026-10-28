(() => {
    "use strict";
    if (typeof document === 'undefined') return;

    document.querySelectorAll("[data-id-flip]").forEach((button) => {
        button.addEventListener("click", () => {
            const card = document.getElementById(button.getAttribute("aria-controls"));
            if (!card) return;

            const showingBack = card.classList.toggle("is-flipped");
            button.setAttribute("aria-pressed", showingBack.toString());
            const label = button.querySelector("span");
            if (label) label.textContent = showingBack ? button.dataset.showFront : button.dataset.showBack;
        });
    });

    document.querySelectorAll("[data-id-number-toggle]").forEach((button) => {
        button.addEventListener("click", () => {
            const number = button.parentElement?.querySelector("[data-id-number]");
            if (!number) return;

            const revealed = button.getAttribute("aria-pressed") === "true";
            number.textContent = revealed ? number.dataset.maskedNumber : number.dataset.fullNumber;
            button.setAttribute("aria-pressed", (!revealed).toString());
            button.textContent = revealed ? button.dataset.showText : button.dataset.hideText;
        });
    });

    const dialog = document.getElementById("idZoomDialog");
    if (!(dialog instanceof HTMLDialogElement)) return;

    const image = dialog.querySelector("[data-id-zoom-image]");
    const title = dialog.querySelector("#idZoomTitle");
    const level = dialog.querySelector("[data-id-zoom-level]");
    const sideButton = dialog.querySelector("[data-id-zoom-side]");
    const viewport = dialog.querySelector(".id-zoom-viewport");
    let scale = 1;
    let side = "front";
    let frontUrl = "";
    let backUrl = "";

    const render = () => {
        image.style.width = `${scale * 100}%`;
        level.textContent = `${Math.round(scale * 100)}%`;
    };

    const setScale = (nextScale) => {
        scale = Math.min(3, Math.max(1, nextScale));
        render();
    };

    const setSide = (nextSide) => {
        side = nextSide;
        image.src = side === "back" ? backUrl : frontUrl;
        image.alt = `${side === "back" ? "Back" : "Front"} of ${title.textContent}`;
        sideButton.innerHTML = `<i class="bi bi-arrow-repeat" aria-hidden="true"></i> ${side === "back" ? sideButton.dataset.showFront : sideButton.dataset.showBack}`;
        setScale(1);
        viewport.scrollTo({ top: 0, left: 0 });
    };

    document.querySelectorAll("[data-id-zoom]").forEach((button) => {
        button.addEventListener("click", () => {
            frontUrl = button.dataset.frontUrl || "";
            backUrl = button.dataset.backUrl || "";
            title.textContent = button.dataset.idTitle || "ID image";
            sideButton.hidden = !backUrl;
            setSide("front");
            dialog.showModal();
        });
    });

    dialog.querySelector("[data-id-zoom-close]")?.addEventListener("click", () => dialog.close());
    dialog.querySelector("[data-id-zoom-in]")?.addEventListener("click", () => setScale(scale + 0.25));
    dialog.querySelector("[data-id-zoom-out]")?.addEventListener("click", () => setScale(scale - 0.25));
    dialog.querySelector("[data-id-zoom-reset]")?.addEventListener("click", () => setScale(1));
    sideButton?.addEventListener("click", () => setSide(side === "front" ? "back" : "front"));

    viewport?.addEventListener("wheel", (event) => {
        event.preventDefault();
        setScale(scale + (event.deltaY < 0 ? 0.25 : -0.25));
    }, { passive: false });

    dialog.addEventListener("click", (event) => {
        if (event.target === dialog) dialog.close();
    });

    dialog.addEventListener("close", () => {
        image.removeAttribute("src");
        setScale(1);
    });
})();

(() => {
    'use strict';

    function initUpload(doc, captureApi, request) {
        const form = doc.getElementById('walletUploadForm');
        if (!form || !captureApi) return null;
        const get = id => doc.getElementById(id);
        const type = get('walletIdType');
        const scanButton = get('walletScanButton');
        const saveButton = get('walletSaveButton');
        const status = get('walletScanStatus');
        const cancelScan = get('walletCancelScan');
        const frontToken = get('walletTempFrontToken');
        const backToken = get('walletTempBackToken');
        const receipt = get('walletScanReceipt');
        const fields = [
            ['firstName', 'walletFirstName'], ['middleName', 'walletMiddleName'],
            ['lastName', 'walletLastName'], ['dateOfBirth', 'walletDateOfBirth'],
            ['idNumber', 'walletIdNumber'], ['address', 'walletAddress'],
            ['sex', 'walletSex'], ['bloodType', 'walletBloodType'], ['suffix', 'walletSuffix']
        ];
        const controls = [type, scanButton, saveButton, get('walletFrontCamera'), get('walletBackCamera'),
            get('walletFrontPhoto'), get('walletBackPhoto')];
        const previewUrls = {};
        let generation = 0;
        let abort = null;
        let submitting = false;
        let reloadRequired = false;
        let camera;

        function feedback(message, error = false) {
            status.textContent = message || '';
            status.classList.toggle('text-danger', error);
        }

        function busy(value) {
            controls.forEach(control => { if (control) control.disabled = value; });
            fields.forEach(([, id]) => { if (get(id)) get(id).readOnly = value; });
            if (cancelScan) cancelScan.hidden = !value;
            if (value) form.setAttribute('aria-busy', 'true');
            else {
                form.removeAttribute('aria-busy');
                scanButton.disabled = !camera?.getFile('front');
                saveButton.disabled = reloadRequired;
            }
        }

        function invalidate() {
            generation++;
            abort?.abort();
            abort = null;
            frontToken.value = backToken.value = receipt.value = '';
            busy(false);
        }

        function preview(side, file) {
            const image = get(side === 'front' ? 'walletFrontPreview' : 'walletBackPreview');
            if (previewUrls[side]) URL.revokeObjectURL(previewUrls[side]);
            previewUrls[side] = file ? URL.createObjectURL(file) : '';
            image.hidden = !file;
            if (file) image.src = previewUrls[side];
            else image.removeAttribute('src');
        }

        camera = captureApi.create({
            form, frontInput: get('walletFrontPhoto'), backInput: get('walletBackPhoto'),
            idType: () => type.value,
            needsBack: () => type.selectedOptions[0]?.dataset.back === 'true',
            onChange(side, file) {
                invalidate();
                feedback('');
                preview(side, file);
                scanButton.disabled = !camera.getFile('front');
            },
            onScan: () => { void scan(); },
            onError: message => feedback(message, true),
            beforeOpen: () => {
                generation++; abort?.abort(); abort = null; busy(false); feedback('');
            }
        });

        async function scan() {
            if (!type.value) { feedback(form.dataset.selectType, true); type.focus(); return; }
            const front = camera.getFile('front');
            if (!front) { feedback(form.dataset.photoRequired, true); return; }
            invalidate();
            camera.stop();
            const current = generation;
            const photoVersion = camera.version;
            const pending = new AbortController();
            abort = pending;
            busy(true);
            feedback(form.dataset.scanWorking);
            const body = new FormData();
            body.append('idType', type.value);
            body.append('frontPhoto', front);
            const back = camera.getFile('back');
            if (back && type.selectedOptions[0]?.dataset.back === 'true') body.append('backPhoto', back);
            const token = form.querySelector('input[name="__RequestVerificationToken"]');
            if (token) body.append('__RequestVerificationToken', token.value);
            const currentSelection = () => generation === current && camera.version === photoVersion;
            try {
                const response = await request(form.dataset.extractUrl, { method: 'POST', body, signal: pending.signal });
                const data = await response.json().catch(() => null);
                if (!currentSelection()) return;
                if (!response.ok || !data || typeof data.success !== 'boolean') {
                    feedback(data?.message || form.dataset.scanError, true);
                    return;
                }
                frontToken.value = data.tempFrontToken || '';
                backToken.value = data.tempBackToken || '';
                receipt.value = data.scanReceipt || '';
                for (const [property, id] of fields) get(id).value = data[property] || '';
                feedback(data.success ? form.dataset.scanComplete : (data.message || form.dataset.scanError), !data.success);
                get('walletFirstName').focus();
            } catch (error) {
                if (currentSelection() && error.name !== 'AbortError') feedback(form.dataset.scanError, true);
            } finally {
                if (generation === current) { abort = null; busy(false); }
            }
        }

        get('walletFrontCamera').addEventListener('click', () => camera.start('front'));
        get('walletBackCamera').addEventListener('click', () => camera.start('back'));
        scanButton.addEventListener('click', () => { void scan(); });
        cancelScan?.addEventListener('click', () => { invalidate(); feedback(''); });
        type.addEventListener('change', () => {
            invalidate();
            camera.clear();
            preview('front', null); preview('back', null);
            fields.forEach(([, id]) => { get(id).value = ''; });
            get('walletBackCapture').hidden = type.selectedOptions[0]?.dataset.back !== 'true';
            feedback('');
        });
        form.addEventListener('submit', async event => {
            event.preventDefault();
            camera.stop();
            if (submitting || abort || reloadRequired) return;
            if (!camera.getFile('front') && !frontToken.value) {
                feedback(form.dataset.photoRequired, true); get('walletFrontCamera').focus(); return;
            }
            if (!form.reportValidity()) return;
            // Construct before disabling controls. The camera's formdata handler supplies the
            // canonical files even on mobile browsers that cannot assign an input's FileList.
            const body = new (doc.defaultView?.FormData || FormData)(form);
            submitting = true;
            let redirecting = false;
            const saveLabel = saveButton.textContent;
            busy(true);
            saveButton.textContent = form.dataset.saving;
            try {
                const response = await request(form.action, { method: 'POST', body, headers: { Accept: 'application/json' } });
                const data = await response.json().catch(() => null);
                if (!data || typeof data.success !== 'boolean' ||
                    (data.success && (!response.ok || typeof data.redirectUrl !== 'string' || !data.redirectUrl)))
                    throw new Error('The save outcome could not be confirmed.');
                if (response.ok && data?.success && data.redirectUrl) {
                    doc.defaultView.location.assign(data.redirectUrl);
                    redirecting = true;
                    return;
                }
                if (data?.resetScan) frontToken.value = backToken.value = receipt.value = '';
                reloadRequired = data?.reloadRequired === true;
                if (get('walletReload')) get('walletReload').hidden = !reloadRequired;
                const errors = Object.values(data?.errors || {}).flat().filter(value => typeof value === 'string');
                feedback([data?.message || form.dataset.saveError, ...new Set(errors)].join(' '), true);
            } catch {
                // A lost response may follow a successful commit. Require a reload before
                // retrying so a manual upload cannot create a duplicate document.
                reloadRequired = true;
                if (get('walletReload')) get('walletReload').hidden = false;
                feedback(form.dataset.uncertainSave || form.dataset.saveError, true);
            } finally {
                if (!redirecting) {
                    submitting = false;
                    saveButton.textContent = saveLabel;
                    busy(false);
                }
            }
        });
        const stop = () => { generation++; abort?.abort(); abort = null; camera.stop(); busy(submitting); };
        doc.defaultView?.addEventListener('pagehide', () => {
            stop();
            Object.values(previewUrls).filter(Boolean).forEach(url => URL.revokeObjectURL(url));
        });
        doc.defaultView?.addEventListener('pageshow', event => {
            if (!event.persisted) return;
            if (submitting || reloadRequired) {
                doc.defaultView.location.reload();
                return;
            }
            preview('front', camera.getFile('front'));
            preview('back', camera.getFile('back'));
            busy(false);
        });
        doc.addEventListener?.('visibilitychange', () => {
            if (!doc.hidden) return;
            generation++; abort?.abort(); abort = null; busy(submitting);
            // The shared camera stops live streams here while preserving an OS picker
            // that intentionally backgrounds the page until its photo is returned.
        });
        get('walletBackCapture').hidden = type.selectedOptions[0]?.dataset.back !== 'true';
        busy(false);
        return { scan, invalidate, camera };
    }

    if (typeof module === 'object' && module.exports) module.exports = { initUpload };
    if (typeof window !== 'undefined' && window.document)
        initUpload(window.document, window.DrmcIdCapture, window.fetch.bind(window));
})();
