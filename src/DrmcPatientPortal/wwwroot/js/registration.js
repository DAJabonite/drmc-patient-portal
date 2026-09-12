$(function () {
    // Step state tracking
    let currentStep = 1;
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

    // ID Type selection handler
    selectIdType.addEventListener('change', function () {
        const val = selectIdType.value;
        if (!val) {
            idGuidanceCard.classList.add('d-none');
            btnGoToCapture.disabled = true;
            return;
        }

        btnGoToCapture.disabled = false;
        idGuidanceCard.classList.remove('d-none');
        guidanceTitle.textContent = val;

        const info = idGuidanceMap[val] || { desc: "Position your ID within the frame.", supports: "Extracts available cardholder information." };
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
        }
    });

    // Nav buttons
    btnGoToCapture.addEventListener('click', function () {
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
        isManualFlow = true;
        isManualEntryInput.value = 'true';
        autofillNotice.classList.add('d-none');
        // Ensure an ID type is selected or fallback to default
        if (!selectIdType.value) {
            selectIdType.value = "Philippine National ID (PhilSys)";
        }
        selectIdType.dispatchEvent(new Event('change'));
        updateProgress(3);
    }

    document.getElementById('btnManualSkip').addEventListener('click', enterManualMode);
    document.getElementById('btnManualSkipFromStep2').addEventListener('click', enterManualMode);

    // Front photo preview
    frontPhotoInput.addEventListener('change', function () {
        if (frontPhotoInput.files && frontPhotoInput.files[0]) {
            const file = frontPhotoInput.files[0];
            const reader = new FileReader();
            reader.onload = function (e) {
                frontPreviewImg.src = e.target.result;
                frontPreviewContainer.classList.remove('d-none');
                frontPlaceholder.classList.add('d-none');
                document.getElementById('frontInputLabel').innerHTML = '<i class="bi bi-arrow-repeat me-1"></i> Retake Front';
                btnTriggerOcr.disabled = false;
            };
            reader.readAsDataURL(file);
        }
    });

    // Back photo preview
    backPhotoInput.addEventListener('change', function () {
        if (backPhotoInput.files && backPhotoInput.files[0]) {
            const file = backPhotoInput.files[0];
            const reader = new FileReader();
            reader.onload = function (e) {
                backPreviewImg.src = e.target.result;
                backPreviewContainer.classList.remove('d-none');
                backPlaceholder.classList.add('d-none');
                document.getElementById('backInputLabel').innerHTML = '<i class="bi bi-arrow-repeat me-1"></i> Retake Back';
            };
            reader.readAsDataURL(file);
        }
    });

    // Trigger genuine local OCR Extraction
    btnTriggerOcr.addEventListener('click', async function () {
        if (!frontPhotoInput.files || !frontPhotoInput.files[0]) {
            showFeedback(2, 'Please capture or select the front of your ID card first.');
            return;
        }

        step2.querySelectorAll('button, input').forEach(control => control.disabled = true);
        step2.setAttribute('aria-busy', 'true');
        document.getElementById('stepFeedback2').classList.add('d-none');
        ocrProcessingState.classList.remove('d-none');

        const formData = new FormData();
        formData.append('idType', selectIdType.value);
        formData.append('frontPhoto', frontPhotoInput.files[0]);
        if (backPhotoInput.files && backPhotoInput.files[0]) {
            formData.append('backPhoto', backPhotoInput.files[0]);
        }

        // Append antiforgery token
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tokenInput) {
            formData.append('__RequestVerificationToken', tokenInput.value);
        }

        try {
            const response = await fetch('?handler=ExtractId', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) throw new Error('ID extraction request failed.');
            const data = await response.json();
            ocrProcessingState.classList.add('d-none');
            btnTriggerOcr.disabled = false;

            if (data.tempFrontToken) {
                document.getElementById('tempFrontToken').value = data.tempFrontToken;
            }
            if (data.tempBackToken) {
                document.getElementById('tempBackToken').value = data.tempBackToken;
            }
            if (data.meanConfidence) {
                document.getElementById('ocrConfidence').value = data.meanConfidence;
            }

            // Populate extracted fields into editable inputs
            if (data.firstName) document.getElementById('txtFirstName').value = data.firstName;
            if (data.middleName) document.getElementById('txtMiddleName').value = data.middleName;
            if (data.lastName) document.getElementById('txtLastName').value = data.lastName;
            if (data.idNumber) document.getElementById('txtIdNumber').value = data.idNumber;
            if (data.dateOfBirth) document.getElementById('txtDob').value = data.dateOfBirth;
            if (data.address) document.getElementById('txtAddress').value = data.address;
            if (data.sex) document.getElementById('txtSex').value = data.sex;
            if (data.bloodType) document.getElementById('txtBloodType').value = data.bloodType;

            autofillNotice.classList.toggle('d-none', !data.success);
            updateProgress(3);
            if (!data.success) showFeedback(3, data.message || 'We could not read all ID details. Please enter or correct your information below.');
        } catch (err) {
            ocrProcessingState.classList.add('d-none');
            btnTriggerOcr.disabled = false;
            autofillNotice.classList.add('d-none');
            updateProgress(3);
            showFeedback(3, 'We could not scan your ID. Please enter your details below, or go back to try again.');
        } finally {
            step2.querySelectorAll('button, input').forEach(control => control.disabled = false);
            step2.removeAttribute('aria-busy');
        }
    });

    form.addEventListener('submit', function (event) {
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
