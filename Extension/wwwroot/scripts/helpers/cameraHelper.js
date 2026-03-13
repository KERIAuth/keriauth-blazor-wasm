// Check/request camera permission using the browser's native media permission system.
// Triggers the browser's camera prompt if permission has not yet been granted.
// Returns true if access is granted, false if denied.
export async function requestCameraPermission() {
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true });
        stream.getTracks().forEach(t => t.stop());
        return true;
    } catch {
        return false;
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
