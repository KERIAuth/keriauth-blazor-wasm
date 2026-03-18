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

    try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true });
        stream.getTracks().forEach(t => t.stop());
        return "granted";
    } catch {
        return "denied";
    }
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
