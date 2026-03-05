// Apply close-focus constraint hint to the video track inside a container.
// Returns true if the constraint was applied, false otherwise.
export async function applyCloseFocusHint(containerSelector) {
    const container = document.querySelector(containerSelector);
    if (!container) return false;

    const video = container.querySelector("video");
    if (!video || !video.srcObject) return false;

    const track = video.srcObject.getVideoTracks()[0];
    if (!track) return false;

    const capabilities = track.getCapabilities();
    if (!capabilities.focusDistance) return false;

    await track.applyConstraints({
        advanced: [{ focusMode: "manual", focusDistance: capabilities.focusDistance.min }]
    });
    return true;
}
