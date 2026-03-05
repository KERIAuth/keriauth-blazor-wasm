// Play a short beep tone using the Web Audio API.
export async function playBeep(frequency, durationMs) {
    const ctx = new AudioContext();
    // Chrome suspends AudioContext when not created from a direct user gesture
    if (ctx.state === "suspended") {
        await ctx.resume();
    }

    const oscillator = ctx.createOscillator();
    const gain = ctx.createGain();

    oscillator.type = "sine";
    oscillator.frequency.value = frequency;
    gain.gain.value = 0.2;

    oscillator.connect(gain);
    gain.connect(ctx.destination);

    oscillator.start();
    oscillator.stop(ctx.currentTime + durationMs / 1000);

    oscillator.onended = () => ctx.close();
}
