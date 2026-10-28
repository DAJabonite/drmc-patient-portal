$(function () {
    // Step state tracking
    let currentStep = 1;
    let recordCodeScanner;
    let idCapture;
    let scanAbort;
    let scanGeneration = 0;
    let isManualFlow = document.getElementById('isManualEntry').value.toLowerCase() === 'true';

    const step1 = document.getElementById('wizardStep1');
    const step2 = document.getElementById('wizardStep2');
    const step3 = document.getElementById('wizardStep3');
    const step4 = document.getElementById('wizardStep4');

    const btnStep1 = document.getElementById('stepBtn1');
    const btnStep2 = document.getElementById('stepBtn2');
    const btnStep3 = document.getElementById('stepBtn3');
    const btnStep4 = document.getElementById('stepBtn4');

    const labelStep1 = document.getElementById('stepLabel1');
    const labelStep2 = document.getElementById('stepLabel2');
    const labelStep3 = document.getElementById('stepLabel3');
    const labelStep4 = document.getElementById('stepLabel4');

    const stepper = document.getElementById('wizardStepperNav');

    const selectIdType = document.getElementById('selectIdType');
    const otherIdFields = document.getElementById('otherGovernmentIdFields');
    const otherIdName = document.getElementById('Input_OtherGovernmentIdName');
    const idGuidanceCard = document.getElementById('idGuidanceCard');
    const guidanceTitle = document.getElementById('guidanceTitle');
    const guidanceText = document.getElementById('guidanceText');
    const guidanceSupports = document.getElementById('guidanceSupports');
    const btnGoToCapture = document.getElementById('btnGoToCapture');
    const displaySelectedId = document.getElementById('displaySelectedId');

    const frontPhotoInput = document.getElementById('frontPhotoInput');
    const backPhotoInput = document.getElementById('backPhotoInput');
    const backCaptureCol = document.getElementById('backCaptureCol');
    const frontCaptureCol = document.getElementById('frontCaptureCol');

    const frontPreviewContainer = document.getElementById('frontPreviewContainer');
    const frontPreviewImg = document.getElementById('frontPreviewImg');
    const frontPlaceholder = document.getElementById('frontPlaceholder');

    const backPreviewContainer = document.getElementById('backPreviewContainer');
    const backPreviewImg = document.getElementById('backPreviewImg');
    const backPlaceholder = document.getElementById('backPlaceholder');

    const btnTriggerOcr = document.getElementById('btnTriggerOcr');
    const ocrProcessingState = document.getElementById('ocrProcessingState');
    const autofillNotice = document.getElementById('autofillNotice');
    const autofillSummary = document.getElementById('autofillSummary');
    const extractedFields = [
        ['firstName', 'txtFirstName', 'First name'],
        ['middleName', 'txtMiddleName', 'Middle name'],
        ['lastName', 'txtLastName', 'Last name'],
        ['idNumber', 'txtIdNumber', 'ID number'],
        ['dateOfBirth', 'txtDob', 'Date of birth'],
        ['address', 'txtAddress', 'Address'],
        ['sex', 'txtSex', 'Sex'],
        ['bloodType', 'txtBloodType', 'Blood type']
    ];

    function resetScanMetadata() {
        document.getElementById('tempFrontToken').value = '';
        document.getElementById('tempBackToken').value = '';
        document.getElementById('ocrConfidence').value = '0';
        autofillNotice.classList.add('d-none');
    }

    const isManualEntryInput = document.getElementById('isManualEntry');

    // ID guidance mapping
    const idGuidanceMap = {
        "Philippine National ID (PhilSys)": {
            desc: "Position the front of your PhilSys National ID within the frame. You will also be prompted for the back of the card.",
            supports: "Extracts: Name, ID number, Date of Birth, Address, Sex (back), Blood type (back)."
        },
        "Driver's License": {
            desc: "Position your LTO Driver's License within the frame. Ensure printed text is glare-free and legible.",
            supports: "Extracts: Name, License number, Date of Birth, Address, Sex, Blood type."
        },
        "Philippine Passport": {
            desc: "Position the passport biodata page within the frame, ensuring the 2-line Machine Readable Zone (MRZ) at the bottom is clear.",
            supports: "Extracts: Full Name, Passport number, Date of Birth, Sex."
        },
        "UMID": {
            desc: "Position the front of your UMID card within the frame, ensuring the CRN and full name are visible.",
            supports: "Extracts: Name, CRN, Date of Birth, Address, Sex."
        },
        "Postal ID": {
            desc: "Position the front of your PHLPost Postal ID within the frame with all details sharply in focus.",
            supports: "Extracts: Name, Postal ID number, Date of Birth, Address."
        },
        "PhilHealth ID": {
            desc: "Position your PhilHealth Identification Card within the frame, keeping your 12-digit number clear.",
            supports: "Extracts: Member Name and 12-digit PhilHealth PIN."
        },
        "SSS / GSIS ID": {
            desc: "Position your SSS or GSIS member card within the frame with card number clearly legible.",
            supports: "Extracts: Member Name and identification number."
        },
        "PRC ID": {
            desc: "Position your PRC Professional ID within the frame with registration number clearly visible.",
            supports: "Extracts: Full Name and PRC registration number."
        }
    };

    const form = document.getElementById('registerForm');
    $.validator.unobtrusive.parse(form);
    const validator = $(form).data('validator');
    // The wizard validates hidden steps explicitly before the final POST.
    $(form).off('submit.validate');
    validator.settings.normalizer = function (value) { return this.name.endsWith('Password') ? value : value.trim(); };
    const fields = document.getElementById('registrationFields');
    const consentValue = document.getElementById('privacyConsentValue');
    const consent = document.getElementById('regConsent');
    const consentToggle = consent?.closest('.form-check');
    const consentElement = document.getElementById('registrationConsent');
    const consentModal = new bootstrap.Modal(consentElement);
    const consentDocument = document.getElementById('consentDocument');
    const acceptConsent = document.getElementById('acceptConsent');
    let accepted = consentValue.value.toLowerCase() === 'true';
    let readToEnd = accepted;
    let submitting = false;

    function showFeedback(step, message) {
        const feedback = document.getElementById('stepFeedback' + step);
        feedback.textContent = message;
        feedback.classList.remove('d-none');
    }

    function updateProgress(step, moveFocus = true) {
        if (step !== 4) recordCodeScanner?.stop();
        if (step !== 2) {
            idCapture?.stop();
            if (scanAbort) cancelIdScan();
        }
        currentStep = step;
        const steps = [step1, step2, step3, step4];
        const btns = [btnStep1, btnStep2, btnStep3, btnStep4];
        steps.forEach((panel, idx) => panel.classList.toggle('d-none', idx + 1 !== step));
        btns.forEach((badge, idx) => {
            const item = badge.parentElement;
            const skipped = isManualFlow && idx === 1 && step > 2;
            item.classList.toggle('is-current', idx + 1 === step);
            item.classList.toggle('is-complete', idx + 1 < step && !skipped);
            item.classList.toggle('is-skipped', skipped);
            item.removeAttribute('aria-current');
            if (idx + 1 === step) item.setAttribute('aria-current', 'step');
            badge.innerHTML = skipped ? '&ndash;' : idx + 1 < step ? '<i class="bi bi-check" aria-hidden="true"></i><span class="visually-hidden">Completed</span>' : String(idx + 1);
        });
        labelStep2.textContent = isManualFlow && step > 2 ? 'Photo skipped' : 'ID photo';
        stepper.style.setProperty('--wizard-progress', ((step - 1) / 3 * 75) + '%');
        step3.querySelector('h2').textContent = isManualFlow ? 'Review your details' : 'Review & confirm extracted details';
        if (moveFocus) {
            const heading = steps[step - 1].querySelector('h2');
            heading.setAttribute('tabindex', '-1');
            heading.focus({ preventScroll: true });
            heading.scrollIntoView({ block: 'start', behavior: 'instant' });
        }
    }

    function validateStep(step) {
        let valid = true;
        let firstInvalid;
        if (step === 1 && selectIdType.value === 'Other government-issued ID' && !otherIdName.value.trim()) {
            showFeedback(1, 'Enter the ID name and government issuer.');
            otherIdName.focus();
            return false;
        }
        for (const input of document.querySelectorAll('#wizardStep' + step + ' [data-val="true"]')) {
            if (!validator.element(input)) { valid = false; firstInvalid ??= input; }
        }
        const feedback = document.getElementById('stepFeedback' + step);
        feedback.classList.toggle('d-none', valid);
        if (!valid) {
            showFeedback(step, 'Please correct the highlighted fields before continuing.');
            firstInvalid.focus();
        }
        return valid;
    }

    function updateConsentGate() {
        if (consentDocument.scrollHeight - consentDocument.clientHeight - consentDocument.scrollTop <= 3) readToEnd = true;
        consent.disabled = !readToEnd;
        acceptConsent.disabled = !readToEnd || !consent.checked;
        document.getElementById('consentReadStatus').textContent = readToEnd
            ? 'You reached the end. Check the agreement box to continue.' : 'Scroll to the end to continue.';
    }
    consentDocument.addEventListener('scroll', updateConsentGate);
    consentDocument.addEventListener('keydown', event => {
        // WebKit on Windows does not provide native Home/End scrolling here.
        if (event.target === consentDocument && (event.key === 'End' || event.key === 'Home')) {
            event.preventDefault();
            consentDocument.scrollTop = event.key === 'End' ? consentDocument.scrollHeight : 0;
            updateConsentGate();
        }
    });
    new ResizeObserver(() => {
        if (consentElement.classList.contains('show')) updateConsentGate();
    }).observe(consentDocument);
    consent.addEventListener('change', updateConsentGate);
    consentToggle?.addEventListener('click', event => {
        if (readToEnd) return;
        event.preventDefault();
        const status = document.getElementById('consentReadStatus');
        status.classList.remove('consent-read-attention');
        void status.offsetWidth;
        status.classList.add('consent-read-attention');
        window.setTimeout(() => status.classList.remove('consent-read-attention'), 700);
    });
    consentElement.addEventListener('shown.bs.modal', () => {
        consentDocument.focus();
        updateConsentGate();
    });
    consentElement.addEventListener('hidden.bs.modal', () => {
        if (accepted) updateProgress(currentStep);
    });
    acceptConsent.addEventListener('click', () => {
        if (!readToEnd || !consent.checked) return;
        accepted = true;
        consentValue.value = 'true';
        fields.disabled = false;
        consentModal.hide();
    });
    document.getElementById('reviewConsent').addEventListener('click', () => consentModal.show());

    // File inputs remain available to assistive technology; visible labels also support keyboard activation.
    for (const label of document.querySelectorAll('[id$="InputLabel"]')) {
        label.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                document.getElementById(label.htmlFor).click();
            }
        });
    }

    // Preserve staged tokens when redisplaying a form with server validation errors.
    let previousIdType = selectIdType.value;
    // ID Type selection handler
    selectIdType.addEventListener('change', function () {
        const val = selectIdType.value;
        if (val !== previousIdType) {
            idCapture?.clear();
            cancelIdScan();
            resetScanMetadata();
        }
        previousIdType = val;
        otherIdFields.hidden = val !== 'Other government-issued ID';
        otherIdName.disabled = otherIdFields.hidden;
        otherIdName.required = !otherIdFields.hidden;
        if (!val) {
            idGuidanceCard.classList.add('d-none');
            btnGoToCapture.disabled = true;
            return;
        }

        btnGoToCapture.disabled = false;
        idGuidanceCard.classList.remove('d-none');
        guidanceTitle.textContent = val;

        const info = idGuidanceMap[val] || { desc: "Capture the side or page with your name and ID number.", supports: "Review all details. You can enter missing information manually if this ID cannot be read." };
        guidanceText.textContent = info.desc;
        guidanceSupports.textContent = info.supports;

        const selectedOption = selectIdType.options[selectIdType.selectedIndex];
        const needsBack = selectedOption.getAttribute('data-back') === 'true';
        if (needsBack) {
            backCaptureCol.classList.remove('d-none');
            frontCaptureCol.className = 'col-md-6';
        } else {
            backCaptureCol.classList.add('d-none');
            frontCaptureCol.className = 'col-12 col-md-8 mx-auto';
            backPhotoInput.value = '';
            backPreviewImg.removeAttribute('src');
            backPreviewContainer.classList.add('d-none');
            backPlaceholder.classList.remove('d-none');
            document.getElementById('backInputLabel').innerHTML = '<i class="bi bi-camera me-1"></i> Snap / Select Back';
        }
    });

    // Nav buttons
    btnGoToCapture.addEventListener('click', function () {
        if (!validateStep(1)) return;
        isManualFlow = false;
        isManualEntryInput.value = 'false';
        displaySelectedId.textContent = selectIdType.value;
        updateProgress(2);
    });

    document.getElementById('btnChangeIdType').addEventListener('click', function () {
        updateProgress(1);
    });

    document.getElementById('btnBackToStep1').addEventListener('click', function () {
        updateProgress(1);
    });

    document.getElementById('btnBackToStep2').addEventListener('click', function () {
        updateProgress(isManualFlow ? 1 : 2);
    });

    document.getElementById('btnGoToStep4').addEventListener('click', function () {
        if (!validateStep(3)) return;
        updateProgress(4);
    });

    document.getElementById('btnBackToStep3').addEventListener('click', function () {
        updateProgress(3);
    });

    // Manual Skip Handlers
    function enterManualMode() {
        if (!validateStep(1)) return;
        isManualFlow = true;
        isManualEntryInput.value = 'true';
        autofillNotice.classList.add('d-none');
        selectIdType.dispatchEvent(new Event('change'));
        updateProgress(3);
    }

    document.getElementById('btnManualSkip').addEventListener('click', enterManualMode);
    document.getElementById('btnManualSkipFromStep2').addEventListener('click', enterManualMode);

    function finishScanUi() {
        ocrProcessingState.classList.add('d-none');
        step2.querySelectorAll('button, input').forEach(control => control.disabled = false);
        btnTriggerOcr.disabled = !idCapture?.getFile('front');
        step2.removeAttribute('aria-busy');
    }

    function cancelIdScan() {
        scanGeneration++;
        scanAbort?.abort();
        scanAbort = null;
        finishScanUi();
    }

    function previewPhoto(side, file) {
        cancelIdScan();
        resetScanMetadata();
        const front = side === 'front';
        const image = front ? frontPreviewImg : backPreviewImg;
        const container = front ? frontPreviewContainer : backPreviewContainer;
        const placeholder = front ? frontPlaceholder : backPlaceholder;
        container.classList.toggle('d-none', !file);
        placeholder.classList.toggle('d-none', !!file);
        document.getElementById(front ? 'frontInputLabel' : 'backInputLabel').textContent =
            file ? (front ? 'Select another front photo' : 'Select another back photo') : (front ? 'Select front photo' : 'Select back photo');
        if (!file) { image.removeAttribute('src'); return; }
        const reader = new FileReader();
        reader.onload = event => {
            if (idCapture.getFile(side) === file) image.src = event.target.result;
        };
        reader.readAsDataURL(file);
    }

    idCapture = DrmcIdCapture.create({
        form, frontInput: frontPhotoInput, backInput: backPhotoInput,
        idType: () => selectIdType.value,
        needsBack: () => selectIdType.selectedOptions[0]?.dataset.back === 'true',
        onChange: previewPhoto,
        onScan: () => btnTriggerOcr.click(),
        beforeOpen: () => { recordCodeScanner?.stop(); cancelIdScan(); },
        onError: message => showFeedback(2, message)
    });
    document.getElementById('btnCameraFront').addEventListener('click', () => idCapture.start('front'));
    document.getElementById('btnCameraBack').addEventListener('click', () => idCapture.start('back'));

    // Trigger genuine local OCR Extraction
    btnTriggerOcr.addEventListener('click', async function () {
        if (scanAbort) return;
        if (!idCapture.getFile('front')) {
            showFeedback(2, 'Please capture or select the front of your ID card first.');
            return;
        }

        idCapture.stop();
        const generation = ++scanGeneration;
        const captureVersion = idCapture.version;
        scanAbort = new AbortController();
        isManualEntryInput.value = 'false';
        resetScanMetadata();
        step2.querySelectorAll('button, input').forEach(control => control.disabled = true);
        step2.setAttribute('aria-busy', 'true');
        document.getElementById('stepFeedback2').classList.add('d-none');
        ocrProcessingState.classList.remove('d-none');

        const formData = new FormData();
        formData.append('idType', selectIdType.value);
        formData.append('frontPhoto', idCapture.getFile('front'));
        if (!backCaptureCol.classList.contains('d-none') && idCapture.getFile('back')) {
            formData.append('backPhoto', idCapture.getFile('back'));
        }

        // Append antiforgery token
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tokenInput) {
            formData.append('__RequestVerificationToken', tokenInput.value);
        }

        try {
            const response = await fetch('?handler=ExtractId', {
                method: 'POST',
                body: formData,
                signal: scanAbort.signal
            });

            const data = await response.json().catch(() => null);
            if (generation !== scanGeneration || captureVersion !== idCapture.version) return;
            scanAbort = null;
            ocrProcessingState.classList.add('d-none');
            btnTriggerOcr.disabled = false;
            if (!response.ok || !data || typeof data.success !== 'boolean') {
                autofillNotice.classList.add('d-none');
                updateProgress(2);
                showFeedback(2, data?.message || 'ID scanning is unavailable. Please try again later.');
                return;
            }

            document.getElementById('tempFrontToken').value = data.tempFrontToken || '';
            document.getElementById('tempBackToken').value = data.tempBackToken || '';
            document.getElementById('ocrConfidence').value = Number.isFinite(data.meanConfidence) ? data.meanConfidence : 0;

            const summary = data.foundSummary || {};
            const filled = [];
            const missing = [];
            const expected = new Set((selectIdType.selectedOptions[0]?.dataset.expectedFields || '').split(','));
            // Replace every scanned field, including empty values, so a rescan cannot mix IDs.
            for (const [property, id, label] of extractedFields) {
                const input = document.getElementById(id);
                input.value = data[property] || '';
                if (summary[label] && input.value) filled.push(label);
                else if (expected.has(property)) missing.push(label);
                validator.element(input);
            }
            document.getElementById('stepFeedback3').classList.add('d-none');
            autofillNotice.classList.toggle('d-none', filled.length === 0);
            autofillNotice.classList.toggle('alert-success', missing.length === 0);
            autofillNotice.classList.toggle('alert-warning', missing.length > 0);
            autofillSummary.textContent = 'Filled from your ID: ' + filled.join(', ') + '. ' +
                (missing.length ? 'Not read: ' + missing.join(', ') + '. Enter missing details; optional fields may stay blank. ' : '') +
                'Please check every entry.';
            updateProgress(3);
            if (!filled.length) showFeedback(3, data.message || 'No ID details could be read. Enter your details below, or go back and try a clearer photo.');
        } catch (err) {
            if (generation !== scanGeneration || err.name === 'AbortError') return;
            scanAbort = null;
            ocrProcessingState.classList.add('d-none');
            btnTriggerOcr.disabled = false;
            autofillNotice.classList.add('d-none');
            updateProgress(3);
            showFeedback(3, 'We could not scan your ID. Please enter your details below, or go back to try again.');
        } finally {
            if (generation === scanGeneration) { scanAbort = null; finishScanUi(); }
        }
    });

    // Hospital record code: manual entry, QR links, and local camera decoding.
    const recordCodeInput = document.getElementById('hospitalRecordCode');
    const recordCodePanel = document.getElementById('hospitalRecordCodePanel');
    const recordCodeStatus = document.getElementById('hospitalRecordCodeStatus');
    const scanButton = document.getElementById('btnScanRecordCode');
    const dobInput = document.getElementById('txtDob');
    const codeAlphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

    function canonicalCode(value) {
        let out = '';
        for (const raw of String(value || '').toUpperCase()) {
            const c = raw === 'O' ? '0' : (raw === 'I' || raw === 'L') ? '1' : raw;
            if (codeAlphabet.includes(c)) out += c;
        }
        return out; // Do not silently truncate an overlong code into a valid one.
    }

    function formatCode(value) {
        const c = canonicalCode(value);
        return c.match(/.{1,5}/g)?.join('-') ?? '';
    }

    function setRecordCode(value, message) {
        recordCodeInput.value = formatCode(value);
        recordCodePanel.classList.toggle('is-filled', [15, 20].includes(canonicalCode(recordCodeInput.value).length));
        recordCodeStatus.textContent = message || '';
        if (recordCodeInput.value) validator.element(recordCodeInput);
    }

    recordCodeInput.addEventListener('input', function () {
        const caret = recordCodeInput.selectionStart === recordCodeInput.value.length;
        const formatted = formatCode(recordCodeInput.value);
        if (formatted !== recordCodeInput.value && caret) recordCodeInput.value = formatted;
        recordCodePanel.classList.toggle('is-filled', [15, 20].includes(canonicalCode(recordCodeInput.value).length));
        recordCodeStatus.textContent = '';
    });
    recordCodeInput.addEventListener('blur', function () {
        if (recordCodeInput.value) recordCodeInput.value = formatCode(recordCodeInput.value);
    });

    // Staff invitation: an Admin-issued code that replaces the hospital record code for staff,
    // who usually have no hospital record. Only one of the two panels is shown at a time.
    const staffPanel = document.getElementById('staffInvitationPanel');
    const staffInput = document.getElementById('staffInvitationCode');
    const recordCodeRequired = recordCodePanel.dataset.codeRequired === 'true';

    function setStaffMode(staff, invite) {
        recordCodePanel.classList.toggle('d-none', staff);
        staffPanel.classList.toggle('d-none', !staff);
        if (staff) {
            recordCodeScanner?.stop();
            recordCodeInput.value = '';
            recordCodePanel.classList.remove('is-filled');
            // jQuery Validate also reads the HTML required attribute, so drop both.
            recordCodeInput.removeAttribute('required');
            $(recordCodeInput).rules('remove', 'required');
            validator.element(recordCodeInput);
            if (invite) staffInput.value = formatCode(invite);
            staffPanel.classList.toggle('is-filled', [15, 20].includes(canonicalCode(staffInput.value).length));
        } else {
            staffInput.value = '';
            staffPanel.classList.remove('is-filled');
            validator.element(staffInput);
            if (recordCodeRequired) {
                recordCodeInput.setAttribute('required', '');
                $(recordCodeInput).rules('add', { required: true, messages: { required: recordCodePanel.dataset.codeError } });
            }
        }
    }

    staffInput.addEventListener('input', function () {
        const caret = staffInput.selectionStart === staffInput.value.length;
        const formatted = formatCode(staffInput.value);
        if (formatted !== staffInput.value && caret) staffInput.value = formatted;
        staffPanel.classList.toggle('is-filled', [15, 20].includes(canonicalCode(staffInput.value).length));
    });
    staffInput.addEventListener('blur', function () {
        if (staffInput.value) staffInput.value = formatCode(staffInput.value);
    });
    document.getElementById('btnUseStaffInvite').addEventListener('click', function () { setStaffMode(true); staffInput.focus(); });
    document.getElementById('btnUseRecordCode').addEventListener('click', function () { setStaffMode(false); recordCodeInput.focus(); });
    if (!staffPanel.classList.contains('d-none')) setStaffMode(true);

    // Codes travel in the URL fragment, which is never sent to the server; remove them from the
    // address bar and history once read.
    const hashParams = new URLSearchParams(window.location.hash.slice(1));
    if (hashParams.has('code') || hashParams.has('invite')) {
        const payload = DrmcRegistrationQr.parsePayload(window.location.href, window.location.href);
        if (payload?.kind === 'invite') {
            setStaffMode(true, payload.code);
            document.getElementById('staffInviteNotice').classList.remove('d-none');
        } else if (payload) {
            setRecordCode(payload.code, 'Filled in from your QR code.');
            document.getElementById('hospitalCodeNotice').classList.remove('d-none');
        } else {
            recordCodeStatus.textContent = 'That QR link is invalid. Type the code on your slip.';
        }
        history.replaceState(null, '', window.location.pathname + window.location.search);
    }

    recordCodeScanner = new DrmcRegistrationQr.CameraScanner({
        video: document.getElementById('recordCodeVideo'),
        container: document.getElementById('recordCodeScanner'),
        button: scanButton,
        stopButton: document.getElementById('btnStopScan'),
        status: recordCodeStatus,
        onScan(text) {
            const payload = DrmcRegistrationQr.parsePayload(text, window.location.href);
            if (payload?.kind === 'invite') {
                setStaffMode(true, payload.code);
                document.getElementById('staffInviteNotice').classList.remove('d-none');
                staffInput.focus();
                return;
            }
            if (payload) setRecordCode(payload.code, 'Code scanned. Check that it matches your slip.');
            else recordCodeStatus.textContent = 'That QR is not a DRMC registration code. Type the code on your slip.';
            recordCodeInput.focus();
        }
    });
    scanButton.addEventListener('click', () => { idCapture.stop(); cancelIdScan(); });
    window.addEventListener('pagehide', () => { recordCodeScanner.stop(); idCapture.stop(); cancelIdScan(); });
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) { recordCodeScanner.stop(); cancelIdScan(); }
    });
    consentElement.addEventListener('show.bs.modal', () => { recordCodeScanner.stop(); idCapture.stop(); cancelIdScan(); });

    form.addEventListener('submit', function (event) {
        recordCodeScanner.stop();
        idCapture.stop();
        if (submitting) { event.preventDefault(); return; }
        if (!accepted) { event.preventDefault(); consentModal.show(); return; }
        // Enter advances the current step rather than posting an unfinished wizard.
        if (currentStep !== 4) {
            event.preventDefault();
            if (currentStep === 1 && validateStep(1)) btnGoToCapture.click();
            if (currentStep === 3 && validateStep(3)) updateProgress(4);
            return;
        }
        validator.settings.ignore = '';
        const valid = validator.form();
        validator.settings.ignore = ':hidden';
        if (!valid) {
            event.preventDefault();
            const input = validator.errorList[0].element;
            const step = Number(input.closest('.wizard-step').id.slice(-1));
            updateProgress(step);
            showFeedback(step, 'Please correct the highlighted fields before continuing.');
            input.focus();
            return;
        }
        // A hospital record code is checked against the date of birth, which is optional otherwise.
        if (recordCodeInput.value.trim() && !dobInput.value) {
            event.preventDefault();
            updateProgress(3);
            showFeedback(3, 'Enter your date of birth to use a hospital record code. It must match your hospital record.');
            dobInput.focus();
            return;
        }
        submitting = true;
        const submit = document.getElementById('registerSubmit');
        submit.disabled = true;
        submit.textContent = 'Creating account…';
        form.setAttribute('aria-busy', 'true');
    });

    // Password eye toggles
    document.querySelectorAll('.pw-toggle').forEach(function (btn) {
        btn.addEventListener('click', function () {
            const targetId = btn.getAttribute('aria-controls');
            const input = document.getElementById(targetId);
            if (!input) return;
            const isPassword = input.getAttribute('type') === 'password';
            input.setAttribute('type', isPassword ? 'text' : 'password');
            btn.setAttribute('aria-pressed', isPassword ? 'true' : 'false');
            btn.setAttribute('aria-label', isPassword ? 'Hide password' : 'Show password');
            const icon = btn.querySelector('i');
            if (icon) {
                icon.className = isPassword ? 'bi bi-eye-slash' : 'bi bi-eye';
            }
        });
    });

    selectIdType.dispatchEvent(new Event('change'));
    updateProgress(1, false);
    if (accepted) {
        fields.disabled = false;
        consent.checked = true;
        consent.disabled = false;
    } else {
        consentModal.show();
    }
    if (form.dataset.serverErrors === 'true' && accepted) {
        const input = form.querySelector('.input-validation-error:not([type="hidden"])');
        updateProgress(input?.closest('.wizard-step') ? Number(input.closest('.wizard-step').id.slice(-1)) : 4);
        (input || document.getElementById('registrationErrors')).focus();
    }
});
