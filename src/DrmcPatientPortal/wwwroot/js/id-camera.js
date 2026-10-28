(() => {
    'use strict';

    const maximumBytes = 5 * 1024 * 1024;
    const supported = new Set(['image/jpeg', 'image/png', 'image/webp']);

    // object-fit: contain can letterbox the video. Map the guide into its actual pixel area.
    function cropRect(width, height, video, guide, margin = .04) {
        const scale = Math.min(video.width / width, video.height / height);
        if (!scale || !Number.isFinite(scale)) throw new Error('Video is not ready.');
        const left = video.left + (video.width - width * scale) / 2;
        const top = video.top + (video.height - height * scale) / 2;
        const padX = guide.width * margin, padY = guide.height * margin;
        const x = Math.max(0, Math.round((guide.left - padX - left) / scale));
        const y = Math.max(0, Math.round((guide.top - padY - top) / scale));
        const right = Math.min(width, Math.round((guide.left + guide.width + padX - left) / scale));
        const bottom = Math.min(height, Math.round((guide.top + guide.height + padY - top) / scale));
        if (right <= x || bottom <= y) throw new Error('The framing guide is outside the video.');
        return { x, y, width: right - x, height: bottom - y };
    }

    class CaptureController {
        constructor(options) {
            this.options = options;
            this.host = options.host || window;
            this.document = this.host.document;
            this.modal = this.document.getElementById('idCameraModal');
            if (!this.modal) throw new Error('The ID camera dialog is missing.');
            this.elements = {};
            for (const name of ['video', 'guide', 'view', 'status', 'hint', 'capture', 'native', 'fallback', 'prompt', 'next', 'skip'])
                this.elements[name] = this.modal.querySelector(`[data-camera-${name}]`);
            this.files = { front: options.frontInput.files?.[0] || null, back: options.backInput.files?.[0] || null };
            this.inputs = { front: options.frontInput, back: options.backInput };
            this.revision = 0;
            this.session = 0;
            this.pending = { front: 0, back: 0 };
            this.stream = null;
            this.active = false;
            this.nativePending = null;
            this.opener = null;
            this.expectedCloses = 0;
            for (const side of ['front', 'back']) this.inputs[side].addEventListener('change', () => {
                const file = this.inputs[side].files?.[0] || null;
                this.stop();
                void this.select(side, file, false);
            });
            options.form.addEventListener('formdata', event => {
                for (const side of ['front', 'back']) {
                    const name = this.inputs[side].name || `Input.${side === 'front' ? 'Front' : 'Back'}Photo`;
                    // Never leave an old browser-selected file behind a newer canonical capture.
                    event.formData.delete(name);
                    if (this.files[side]) event.formData.set(name, this.files[side], this.files[side].name);
                }
            });
            this.elements.capture.addEventListener('click', () => void this.capture());
            this.elements.fallback.addEventListener('click', () => this.openNative());
            this.elements.next.addEventListener('click', () => this.start('back'));
            this.elements.skip.addEventListener('click', () => this.finish());
            this.elements.native.addEventListener('change', () => {
                const request = this.nativePending;
                this.nativePending = null;
                const file = this.elements.native.files?.[0];
                this.elements.native.value = '';
                if (!request || request.session !== this.session || !file) { this.stop(); return; }
                void this.select(request.side, file, true);
            });
            this.modal.querySelectorAll('[data-camera-cancel]').forEach(button => button.addEventListener('click', () => this.stop()));
            this.modal.addEventListener('cancel', event => { event.preventDefault(); this.stop(); });
            this.modal.addEventListener('close', () => {
                // close() queues its event. A front-to-back transition may already have
                // opened a new session when the old dialog's close event is delivered.
                if (this.expectedCloses > 0) { this.expectedCloses--; return; }
                if (this.active) this.stop();
            });
            this.host.addEventListener('pagehide', () => this.stop());
            this.host.addEventListener('orientationchange', () => { if (!this.nativePending) this.stop(); });
            this.host.addEventListener('resize', () => { if (this.active && this.stream) this.fitGuide(); });
            this.document.addEventListener('visibilitychange', () => {
                // Opening the OS camera backgrounds the browser; its eventual file is still intentional.
                if (this.document.hidden && !this.nativePending) this.stop();
            });
        }

        get version() { return this.revision; }
        getFile(side) { return this.files[side] || null; }
        text(key, fallback) { return this.modal.dataset[key] || fallback; }
        status(message) { this.elements.status.textContent = message; }

        fitGuide() {
            const video = this.elements.video;
            const bounds = video.getBoundingClientRect();
            if (!video.videoWidth || !video.videoHeight) return;
            const scale = Math.min(bounds.width / video.videoWidth, bounds.height / video.videoHeight);
            const ratio = this.options.idType() === 'Philippine Passport' ? 1.42 : 1.586;
            const width = Math.min(video.videoWidth * scale * .86, video.videoHeight * scale * .82 * ratio);
            this.elements.guide.style.width = `${width}px`;
            this.elements.guide.style.height = `${width / ratio}px`;
        }

        releaseStream() {
            this.stream?.getTracks().forEach(track => track.stop());
            this.stream = null;
            this.elements.video.pause();
            this.elements.video.srcObject = null;
            this.elements.capture.disabled = true;
        }

        stop() {
            this.active = false;
            this.session++;
            this.revision++;
            this.pending.front++;
            this.pending.back++;
            this.nativePending = null;
            this.releaseStream();
            if (this.modal.open) {
                this.expectedCloses++;
                this.modal.close();
            }
            this.opener?.focus();
        }

        assign(side, file) {
            this.files[side] = file;
            const input = this.inputs[side];
            input.value = '';
            try {
                const transfer = new this.host.DataTransfer();
                if (file) transfer.items.add(file);
                input.files = transfer.files;
            } catch { /* formdata supplies canonical files when FileList assignment is unsupported. */ }
            this.options.onChange?.(side, file);
        }

        clear() {
            this.stop();
            this.assign('front', null);
            this.assign('back', null);
        }

        start(side = 'front') {
            if (!this.options.idType()) { this.options.onError?.(this.text('selectType', 'Select your ID type first.')); return; }
            const focused = this.document.activeElement;
            const opener = this.modal.contains?.(focused) ? this.opener : focused;
            this.stop();
            this.options.beforeOpen?.();
            this.side = side;
            this.opener = opener;
            this.active = true;
            this.elements.prompt.hidden = true;
            this.elements.view.hidden = false;
            this.elements.capture.hidden = false;
            this.elements.fallback.hidden = true;
            this.elements.hint.textContent = side === 'back'
                ? this.text('backHint', 'Fit the back of your ID inside the guide.')
                : this.text('frontHint', 'Fit the front of your ID inside the guide. Keep every printed detail visible.');
            this.elements.guide.style.setProperty('--id-camera-ratio', this.options.idType() === 'Philippine Passport' ? '1.42' : '1.586');
            // Keep this direct call in the user gesture for browsers without getUserMedia.
            if (this.host.isSecureContext === false || !this.host.navigator.mediaDevices?.getUserMedia || !this.modal.showModal) {
                this.openNative();
                return;
            }
            if (!this.modal.open) this.modal.showModal();
            this.status(this.text('opening', 'Opening camera…'));
            void this.openStream(this.session);
        }

        async openStream(session) {
            const current = () => this.active && session === this.session;
            try {
                const stream = await this.host.navigator.mediaDevices.getUserMedia({
                    audio: false,
                    video: { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1080 } }
                });
                if (!current()) { stream.getTracks().forEach(track => track.stop()); return; }
                this.stream = stream;
                stream.getTracks().forEach(track => track.addEventListener?.('ended', () => {
                    if (current()) this.cameraError(this.text('deviceError', 'The camera stopped. Use your phone camera or choose a photo.'));
                }, { once: true }));
                const video = this.elements.video;
                video.srcObject = stream;
                await video.play();
                if (!current()) return;
                const ready = () => {
                    if (!current() || !video.videoWidth || !video.videoHeight || video.readyState < 2) return;
                    this.fitGuide();
                    this.elements.capture.disabled = false;
                    this.status(this.text('ready', 'Keep your ID steady, then capture the photo.'));
                };
                video.addEventListener('loadeddata', ready, { once: true });
                ready();
            } catch (error) {
                if (!current()) return;
                this.cameraError(error.name === 'NotAllowedError'
                    ? this.text('permissionError', 'Camera permission was denied. Use your phone camera or choose a photo.')
                    : this.text('deviceError', 'The camera could not be opened. Use your phone camera or choose a photo.'));
            }
        }

        cameraError(message) {
            this.releaseStream();
            this.status(message);
            this.elements.fallback.hidden = false;
            this.elements.fallback.focus();
        }

        openNative() {
            this.releaseStream();
            this.nativePending = { side: this.side, session: this.session };
            this.elements.native.value = '';
            this.elements.native.click();
        }

        async jpeg(source, width, height, area) {
            const canvas = this.document.createElement('canvas');
            const region = area || { x: 0, y: 0, width, height };
            const scale = Math.min(1, 2048 / Math.max(region.width, region.height));
            canvas.width = Math.max(1, Math.round(region.width * scale));
            canvas.height = Math.max(1, Math.round(region.height * scale));
            try {
                const context = canvas.getContext('2d');
                if (!context) throw new Error('Canvas unavailable.');
                context.fillStyle = '#fff';
                context.fillRect?.(0, 0, canvas.width, canvas.height);
                context.drawImage(source, region.x, region.y, region.width, region.height, 0, 0, canvas.width, canvas.height);
                const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', .92));
                if (!blob || blob.size === 0 || blob.size > maximumBytes) throw new Error('Image encoding failed.');
                return new this.host.File([blob], `id-${this.side || 'photo'}.jpg`, { type: 'image/jpeg', lastModified: Date.now() });
            } finally { canvas.width = canvas.height = 0; }
        }

        async loadImage(file) {
            const url = this.host.URL.createObjectURL(file);
            try {
                return await new Promise((resolve, reject) => {
                    const img = new this.host.Image();
                    img.onload = () => resolve(img); img.onerror = reject; img.src = url;
                });
            } finally { this.host.URL.revokeObjectURL(url); }
        }

        async normalize(file) {
            if (!file) return null;
            let image;
            try {
                if (this.host.createImageBitmap) {
                    try { image = await this.host.createImageBitmap(file, { imageOrientation: 'from-image' }); }
                    catch { image = await this.loadImage(file); }
                } else image = await this.loadImage(file);
                return await this.jpeg(image, image.width || image.naturalWidth, image.height || image.naturalHeight);
            } catch {
                // The server still validates magic bytes, encoded dimensions, animation and decoding.
                if (supported.has(file.type) && file.size > 0 && file.size <= maximumBytes) return file;
                throw new Error(this.text(file.size > maximumBytes ? 'sizeError' : 'formatError',
                    file.size > maximumBytes ? 'Choose a photo that can be reduced to 5 MB or less.' : 'Choose a JPEG, PNG or WebP photo.'));
            } finally { image?.close?.(); }
        }

        async select(side, file, scan) {
            const pending = ++this.pending[side];
            const session = this.session;
            this.revision++;
            this.assign(side, null);
            if (side === 'front') { this.pending.back++; this.assign('back', null); }
            try {
                const normalized = await this.normalize(file);
                if (pending !== this.pending[side] || session !== this.session) return;
                this.assign(side, normalized);
                if (scan && normalized) this.accepted(side);
            } catch (error) {
                if (pending !== this.pending[side] || session !== this.session) return;
                this.options.onError?.(error.message);
                if (scan && this.active) this.cameraError(error.message);
            }
        }

        async capture() {
            if (!this.active || this.elements.capture.disabled) return;
            const session = this.session;
            this.elements.capture.disabled = true;
            try {
                const video = this.elements.video;
                this.fitGuide();
                const region = cropRect(video.videoWidth, video.videoHeight,
                    video.getBoundingClientRect(), this.elements.guide.getBoundingClientRect());
                const file = await this.jpeg(video, video.videoWidth, video.videoHeight, region);
                if (!this.active || session !== this.session) return;
                this.revision++;
                this.pending[this.side]++;
                if (this.side === 'front') { this.pending.back++; this.assign('back', null); }
                this.assign(this.side, file);
                this.accepted(this.side);
            } catch {
                if (!this.active || session !== this.session) return;
                this.elements.capture.disabled = false;
                this.status(this.text('captureError', 'Could not capture the photo. Try again or choose a photo.'));
                this.elements.fallback.hidden = false;
            }
        }

        accepted(side) {
            this.releaseStream();
            if (side === 'front' && this.options.needsBack()) {
                this.elements.view.hidden = true;
                this.elements.capture.hidden = true;
                this.elements.fallback.hidden = true;
                this.elements.prompt.hidden = false;
                this.status('');
                if (!this.modal.open) this.modal.showModal();
                this.elements.next.focus();
            } else this.finish();
        }

        finish() {
            const hasFront = !!this.files.front;
            this.stop();
            if (hasFront) this.options.onScan?.();
            else this.options.onError?.(this.text('selectFront', 'Capture or select the front of your ID first.'));
        }
    }

    const api = { create: options => new CaptureController(options), cropRect };
    if (typeof module === 'object' && module.exports) module.exports = api;
    else window.DrmcIdCapture = api;
})();
