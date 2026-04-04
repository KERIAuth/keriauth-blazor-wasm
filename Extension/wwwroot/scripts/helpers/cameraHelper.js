export function getExtensionId() {
    return chrome.runtime.id;
}

// chrome:// URLs cannot be opened via HTML links; must use chrome.tabs.create
export function openInNewTab(url) {
    chrome.tabs.create({ url });
}

// Check/request camera permission using the browser's native media permission system.
// Returns "granted", "denied" (prompt dismissed), or "blocked" (blocked in site settings).
export async function requestCameraPermission() {
    try {
        const result = await navigator.permissions.query({ name: "camera" });
        if (result.state === "denied") {
            return "blocked";
        }
    } catch {
        // permissions.query may not support "camera" in all contexts; fall through to getUserMedia
    }

    // Enumerate devices first to initialise the media subsystem — without this,
    // getUserMedia can fail in extension contexts when scripts are lazily loaded.
    try {
        await navigator.mediaDevices.enumerateDevices();
    } catch {
        // non-fatal; proceed to getUserMedia
    }

    try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true });
        const track = stream.getVideoTracks()[0];

        // Wait for the track to be live and producing frames before releasing.
        // This ensures the media subsystem is fully initialized so the next
        // getUserMedia call (from BarcodeReader) succeeds reliably.
        if (track) {
            if (track.readyState !== "live" || track.muted) {
                await Promise.race([
                    new Promise(resolve => { track.onunmute = resolve; }),
                    new Promise((_, reject) => setTimeout(() => reject(new Error("unmute timeout")), 5000))
                ]).catch(() => { /* proceed even on timeout */ });
            }

            try {
                const capture = new ImageCapture(track);
                await capture.grabFrame();
            } catch {
                // ImageCapture may fail on some devices; permission is still granted
            }
        }

        stream.getTracks().forEach(t => t.stop());
        return "granted";
    } catch {
        return "denied";
    }
}

const virtualCameraPattern = /\b(virtual|obs|snap|manycam|xsplit|droidcam|iriun|epoccam|ndi)\b/i;

// Returns video input devices filtered to exclude known virtual cameras.
// Each entry is { deviceId, label }. Requires camera permission to be granted first.
export async function getPhysicalCameraDevices() {
    const devices = await navigator.mediaDevices.enumerateDevices();
    return devices
        .filter(d => d.kind === "videoinput" && !virtualCameraPattern.test(d.label))
        .map(d => ({ deviceId: d.deviceId, label: d.label }));
}

// Apply continuous autofocus to the video track inside a container.
// Returns true if the constraint was applied, false otherwise.
export async function applyAutoFocus(containerSelector) {
    const container = document.querySelector(containerSelector);
    if (!container) return false;

    const video = container.querySelector("video");
    if (!video || !video.srcObject) return false;

    const track = video.srcObject.getVideoTracks()[0];
    if (!track) return false;

    const capabilities = track.getCapabilities();
    if (!capabilities.focusMode || !capabilities.focusMode.includes("continuous")) return false;

    await track.applyConstraints({
        advanced: [{ focusMode: "continuous" }]
    });
    return true;
}
