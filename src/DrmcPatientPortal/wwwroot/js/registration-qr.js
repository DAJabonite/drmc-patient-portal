(function () {
    'use strict';

    // Decoder: bundled jsQR 1.4.0, Apache-2.0 (see lib/jsqr/LICENSE).

    // Match HospitalRecordCodes.Normalize: reject extra characters instead of stripping
    // unrelated QR text into something that merely has the right length.
    function normalize(value) {
        if (typeof value !== 'string' || !value.length || value.length > 64 ||
            !/^[0-9a-z \t\-\u2010-\u2014]+$/i.test(value)) return null;
        const code = value.replace(/[ \t\-\u2010-\u2014]/g, '').toUpperCase()
            .replace(/O/g, '0').replace(/[IL]/g, '1');
        if (![15, 20].includes(code.length) || !/^[0-9ABCDEFGHJKMNPQRSTVWXYZ]+$/.test(code)) return null;
        return code.match(/.{5}/g).join('-');
    }

    function parsePayload(text, registrationUrl) {
        const rawCode = normalize(text);
        if (rawCode) return { kind: 'code', code: rawCode };
        if (typeof text !== 'string' || text.length > 2048) return null;
        try {
            const url = new URL(text);
            const page = new URL(registrationUrl);
            if (!['http:', 'https:'].includes(url.protocol) ||
                url.pathname.replace(/\/$/, '') !== page.pathname.replace(/\/$/, '')) return null;
            const params = new URLSearchParams(url.hash.slice(1));
            const keys = ['code', 'invite'].filter(key => params.has(key));
            if (keys.length !== 1 || params.getAll(keys[0]).length !== 1) return null;
            const code = normalize(params.get(keys[0]));
            return code ? { kind: keys[0], code } : null;
        } catch { return null; }
    }

    class CameraScanner {
        constructor({ video, container, button, stopButton, status, onScan, host = window }) {
            Object.assign(this, { video, container, button, stopButton, status, onScan, host });
            this.session = 0;
            this.active = false;
            this.stream = null;
            this.timer = null;
            this.canvas = null;
            button.addEventListener('click', () => {
                if (this.active) this.cancel();
                else void this.start();
            });
            stopButton.addEventListener('click', () => this.cancel());
        }

        cancel() {
            this.stop('Scanning stopped. You can type the code instead.');
            this.button.focus();
        }

        stop(message) {
            const wasActive = this.active;
            this.active = false;
            this.session++;
            if (this.timer !== null) this.host.clearTimeout(this.timer);
            this.timer = null;
            this.stream?.getTracks().forEach(track => track.stop());
            this.stream = null;
            this.video.pause();
            this.video.srcObject = null;
            if (this.canvas) this.canvas.width = this.canvas.height = 0;
            this.canvas = null;
            if (this.host.document.activeElement === this.stopButton) this.button.focus();
            this.container.classList.add('d-none');
            this.button.setAttribute('aria-expanded', 'false');
            if (message !== undefined || wasActive) this.status.textContent = message || '';
        }

        async start() {
            if (this.active) return;
            const { host, video } = this;
            if (!host.isSecureContext || !host.navigator.mediaDevices?.getUserMedia) {
                this.status.textContent = 'Camera access is unavailable here. Type the code on your slip.';
                return;
            }
            this.active = true;
            const session = ++this.session;
            const current = () => this.active && this.session === session;
            this.container.classList.remove('d-none');
            this.button.setAttribute('aria-expanded', 'true');
            this.status.textContent = 'Opening camera…';
            this.stopButton.focus();
            try {
                const stream = await host.navigator.mediaDevices.getUserMedia({
                    video: { facingMode: { ideal: 'environment' } }, audio: false
                });
                // A permission prompt may resolve after Stop or navigation to another step.
                if (!current()) { stream.getTracks().forEach(track => track.stop()); return; }
                this.stream = stream;
                stream.getVideoTracks().forEach(track => track.addEventListener('ended', () => {
                    if (current()) this.stop('The camera stopped. Try again or type the code on your slip.');
                }, { once: true }));
                video.srcObject = stream;
                await video.play();
                if (!current()) return;
                let detector = null;
                try {
                    if (host.BarcodeDetector && (await host.BarcodeDetector.getSupportedFormats()).includes('qr_code'))
                        detector = new host.BarcodeDetector({ formats: ['qr_code'] });
                } catch { /* The bundled decoder also works without native QR detection. */ }
                if (!current()) return;
                this.status.textContent = 'Point your camera at the QR code on your slip.';
                const tick = async () => {
                    if (!current()) return;
                    try {
                        let text;
                        if (video.readyState >= 2 && video.videoWidth && video.videoHeight) {
                            if (detector) {
                                try { text = (await detector.detect(video))[0]?.rawValue; }
                                catch { detector = null; }
                            }
                            if (!current()) return;
                            if (!text) {
                                this.canvas ??= host.document.createElement('canvas');
                                const scale = Math.min(1, 960 / Math.max(video.videoWidth, video.videoHeight));
                                this.canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
                                this.canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
                                const context = this.canvas.getContext('2d', { willReadFrequently: true });
                                context.drawImage(video, 0, 0, this.canvas.width, this.canvas.height);
                                const frame = context.getImageData(0, 0, this.canvas.width, this.canvas.height);
                                text = host.jsQR(frame.data, frame.width, frame.height)?.data;
                            }
                        }
                        if (!current()) return;
                        if (text) {
                            this.stop();
                            this.onScan(text);
                            return;
                        }
                    } catch {
                        if (current()) this.stop('Could not read the camera. Try again or type the code on your slip.');
                        return;
                    }
                    // Schedule only after decoding finishes; never overlap frame reads.
                    if (current()) this.timer = host.setTimeout(tick, 250);
                };
                void tick();
            } catch (error) {
                if (!current()) return;
                const message = error.name === 'NotAllowedError'
                    ? 'Camera permission was denied. Allow camera access or type the code on your slip.'
                    : error.name === 'NotFoundError'
                        ? 'No camera was found. Type the code on your slip.'
                        : 'The camera could not be opened. Try again or type the code on your slip.';
                this.stop(message);
                this.button.focus();
            }
        }
    }

    const api = { normalize, parsePayload, CameraScanner };
    if (typeof module === 'object' && module.exports) module.exports = api;
    else window.DrmcRegistrationQr = api;
})();
