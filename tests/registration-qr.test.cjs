const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { normalize, parsePayload, CameraScanner } = require('../src/DrmcPatientPortal/wwwroot/js/registration-qr.js');
const jsQR = require('../src/DrmcPatientPortal/wwwroot/lib/jsqr/jsQR.js');
const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'qr-fixtures/hospital-code.json'), 'utf8').replace(/^\uFEFF/, ''));
const page = 'https://portal.test/Identity/Account/Register';
const code = '7KQ2M-X9D4T-H3VNP-A6B8C';

test('current and legacy codes follow server normalization', () => {
    assert.equal(normalize(code.toLowerCase()), code);
    assert.equal(normalize('7KQ2M X9D4T H3VNP'), '7KQ2M-X9D4T-H3VNP');
    assert.equal(normalize('o1l0i\tABCDE\u201012345\u201467890'), '01101-ABCDE-12345-67890');
    for (const value of [null, '', '7KQ2M-X9D4T-H3VNP-A6B8C-X', code + '!', code + '\n',
        code.replace('A', 'U'), code.replace('A', 'ß'), ' '.repeat(65) + code]) {
        assert.equal(normalize(value), null, String(value));
    }
});

test('registration QR links and raw codes preserve patient and staff modes', () => {
    assert.deepEqual(parsePayload(fixture.payload, page), { kind: 'code', code });
    assert.deepEqual(parsePayload(page + '#invite=' + code, page), { kind: 'invite', code });
    assert.deepEqual(parsePayload('7KQ2MX9D4TH3VNP', page), { kind: 'code', code: '7KQ2M-X9D4T-H3VNP' });
    // Codes may be issued under the public portal host while tested on localhost.
    assert.deepEqual(parsePayload(fixture.payload, 'http://localhost:5095/Identity/Account/Register'), { kind: 'code', code });
    for (const value of ['unrelated text', 'https://example.test/',
        page + '?code=' + code, page + '#code=' + code + '&invite=' + code,
        page + '#code=' + code + '&code=' + code, page + '#invite=bad',
        'https://portal.test/other#code=' + code, 'javascript:alert(1)#code=' + code,
        'data:text/plain,' + code, page + '#code=' + encodeURIComponent(code + '!')]) {
        assert.equal(parsePayload(value, page), null, value);
    }
});

// QRCoder ECC Q module matrix for the same URL shape used on an issued slip.
// Render actual RGBA camera pixels and exercise the vendored decoder, without a
// physical camera, browser permissions, real patient images, or npm dependencies.
function frame() {
    const size = fixture.modules.length * 4;
    const data = new Uint8ClampedArray(size * size * 4);
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
        const offset = (y * size + x) * 4;
        const pixel = fixture.modules[Math.floor(y / 4)][Math.floor(x / 4)] === '1' ? 0 : 255;
        data[offset] = data[offset + 1] = data[offset + 2] = pixel;
        data[offset + 3] = 255;
    }
    return { data, width: size, height: size };
}

function element(hidden = false) {
    const classes = new Set(hidden ? ['d-none'] : []);
    return {
        textContent: '', attributes: {}, listeners: {}, focused: false,
        classList: { add: value => classes.add(value), remove: value => classes.delete(value), contains: value => classes.has(value) },
        addEventListener(name, fn) { this.listeners[name] = fn; },
        setAttribute(name, value) { this.attributes[name] = value; },
        focus() { this.focused = true; }
    };
}

function deferred() {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}

async function settle() { await new Promise(resolve => setImmediate(resolve)); }

function harness(options = {}) {
    const pixels = frame();
    const track = { stopped: 0, listeners: {}, stop() { this.stopped++; }, addEventListener(name, fn) { this.listeners[name] = fn; } };
    const stream = { getTracks: () => [track], getVideoTracks: () => [track] };
    const timers = new Map();
    let timerId = 0, requests = 0, decodedFrames = 0;
    const received = [];
    const video = { ...element(), readyState: 2, videoWidth: pixels.width, videoHeight: pixels.height,
        srcObject: null, play: () => options.playPromise || Promise.resolve(), pause() {} };
    const container = element(true), button = element(), stopButton = element(), status = element();
    const host = {
        isSecureContext: true,
        navigator: { mediaDevices: { async getUserMedia(constraints) {
            requests++;
            assert.equal(constraints.video.facingMode.ideal, 'environment');
            assert.equal(constraints.audio, false);
            if (options.cameraError) throw Object.assign(new Error(), { name: options.cameraError });
            return options.mediaPromise || stream;
        } } },
        document: { createElement(tag) {
            assert.equal(tag, 'canvas');
            return { width: 0, height: 0, getContext: () => ({ drawImage() {}, getImageData: () => pixels }) };
        } },
        jsQR(...args) { decodedFrames++; return options.decode ? options.decode(...args) : jsQR(...args); },
        setTimeout(fn) { const id = ++timerId; timers.set(id, fn); return id; },
        clearTimeout(id) { timers.delete(id); }
    };
    if (options.native) host.BarcodeDetector = options.native;
    const scanner = new CameraScanner({ host, video, container, button, stopButton, status, onScan: text => received.push(text) });
    return { host, scanner, stream, track, video, container, button, stopButton, status, received, timers,
        requests: () => requests, decodedFrames: () => decodedFrames,
        async nextFrame() {
            const [id, callback] = timers.entries().next().value;
            timers.delete(id);
            await callback();
        }
    };
}

test('real bundled decoder reads a hospital QR from synthetic camera frames', async () => {
    const h = harness();
    await h.scanner.start();
    await settle();
    assert.deepEqual(h.received, [fixture.payload]);
    assert.equal(h.decodedFrames(), 1);
    assert.equal(h.track.stopped, 1);
    assert.equal(h.video.srcObject, null);
    assert.equal(h.scanner.active, false);
    assert.equal(h.container.classList.contains('d-none'), true);
    assert.equal(h.button.attributes['aria-expanded'], 'false');
    assert.equal(h.timers.size, 0);
});

test('native detector handles a QR without invoking fallback', async () => {
    class Native {
        static async getSupportedFormats() { return ['qr_code']; }
        async detect() { return [{ rawValue: fixture.payload }]; }
    }
    const h = harness({ native: Native });
    await h.scanner.start();
    await settle();
    assert.deepEqual(h.received, [fixture.payload]);
    assert.equal(h.decodedFrames(), 0);
    assert.equal(h.track.stopped, 1);
});

for (const failure of ['formats', 'unsupported', 'construct', 'detect', 'empty']) {
    test(`bundled fallback reads camera pixels when native detection is ${failure}`, async () => {
        class Native {
            static async getSupportedFormats() {
                if (failure === 'formats') throw new Error('unavailable');
                return failure === 'unsupported' ? [] : ['qr_code'];
            }
            constructor() { if (failure === 'construct') throw new Error('unavailable'); }
            async detect() {
                if (failure === 'detect') throw new Error('unavailable');
                return [];
            }
        }
        const h = harness({ native: Native });
        await h.scanner.start();
        await settle();
        assert.deepEqual(h.received, [fixture.payload]);
        assert.equal(h.track.stopped, 1);
    });
}

for (const [name, message] of [['NotAllowedError', /permission was denied/], ['NotFoundError', /No camera/], ['NotReadableError', /could not be opened/]]) {
    test(`camera ${name} provides manual-entry guidance`, async () => {
        const h = harness({ cameraError: name });
        await h.scanner.start();
        assert.match(h.status.textContent, message);
        assert.equal(h.scanner.active, false);
        assert.equal(h.container.classList.contains('d-none'), true);
        assert.equal(h.button.focused, true);
    });
}

for (const unavailable of ['insecure', 'missing API']) {
    test(`camera ${unavailable} keeps manual entry available`, async () => {
        const h = harness();
        if (unavailable === 'insecure') h.host.isSecureContext = false;
        else h.host.navigator.mediaDevices = undefined;
        await h.scanner.start();
        assert.equal(h.requests(), 0);
        assert.match(h.status.textContent, /unavailable.*Type the code/);
    });
}

test('duplicate starts do not open multiple cameras; late permission closes after cancellation', async () => {
    const media = deferred();
    const h = harness({ mediaPromise: media.promise });
    const starting = h.scanner.start();
    await h.scanner.start();
    assert.equal(h.requests(), 1);
    assert.equal(h.button.attributes['aria-expanded'], 'true');
    assert.equal(h.stopButton.focused, true);
    h.stopButton.listeners.click();
    media.resolve(h.stream);
    await starting;
    assert.equal(h.track.stopped, 1);
    assert.equal(h.video.srcObject, null);
    assert.equal(h.received.length, 0);
    assert.equal(h.button.focused, true);
});

test('late detector results cannot fill a code after stop', async () => {
    const result = deferred();
    class Native {
        static async getSupportedFormats() { return ['qr_code']; }
        detect() { return result.promise; }
    }
    const h = harness({ native: Native });
    await h.scanner.start();
    h.scanner.stop();
    result.resolve([{ rawValue: fixture.payload }]);
    await settle();
    assert.equal(h.received.length, 0);
    assert.equal(h.track.stopped, 1);
    assert.equal(h.timers.size, 0);
});

test('frames are polled sequentially and stopping clears the next attempt', async () => {
    const h = harness({ decode: () => null });
    await h.scanner.start();
    assert.equal(h.timers.size, 1);
    await h.nextFrame();
    assert.equal(h.decodedFrames(), 2);
    assert.equal(h.timers.size, 1);
    h.button.listeners.click();
    assert.equal(h.timers.size, 0);
    assert.equal(h.track.stopped, 1);
    assert.match(h.status.textContent, /Scanning stopped/);
});

test('stopping during video startup closes the stream', async () => {
    const play = deferred();
    const h = harness({ playPromise: play.promise });
    const starting = h.scanner.start();
    await settle();
    h.scanner.stop();
    play.resolve();
    await starting;
    assert.equal(h.track.stopped, 1);
    assert.equal(h.video.srcObject, null);
    assert.equal(h.received.length, 0);
});

test('unexpected camera ending closes preview and permits another attempt', async () => {
    const h = harness({ decode: () => null });
    await h.scanner.start();
    h.track.listeners.ended();
    assert.equal(h.scanner.active, false);
    assert.equal(h.timers.size, 0);
    assert.match(h.status.textContent, /camera stopped/);
    await h.scanner.start();
    assert.equal(h.requests(), 2);
    h.scanner.stop();
});

test('decode errors close camera and explain manual entry', async () => {
    const h = harness({ decode: () => { throw new Error('unreadable frame'); } });
    h.host.document.activeElement = h.stopButton;
    await h.scanner.start();
    assert.equal(h.scanner.active, false);
    assert.equal(h.track.stopped, 1);
    assert.match(h.status.textContent, /Could not read the camera/);
    assert.equal(h.button.focused, true);
});
