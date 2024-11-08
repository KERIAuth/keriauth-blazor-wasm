// TODO P0 remove this file
// Debounce function to delay calls
function debounce(func: (...args: any[]) => void, delay: number) {
    let timer: number | undefined;
    return (...args: any[]) => {
        if (timer !== undefined) {
            clearTimeout(timer);
        }
        timer = window.setTimeout(() => func(...args), delay);
    };
}

export function registerInactivityTimerResetEvents(dotNetReference: any) {
    // Debounced function for ResetInactivityTimer with a delay of 300 milliseconds
    const debouncedResetInactivityTimer = debounce(() => {
        chrome.runtime.sendMessage({ action: 'resetInactivityTimer' });

        // TODO P0 Remove this old way:
        dotNetReference.invokeMethodAsync("ResetInactivityTimer")
            .then((result: any) => {
                // console.log("ack activity to reset timer?");
            })
            .catch((error: any) => {
                console.error(error);
            });
    }, 300); // Adjust delay as needed

    // Attach debounced function to mousemove and keydown events
    document.onmousemove = debouncedResetInactivityTimer;
    document.onkeydown = debouncedResetInactivityTimer;
}

// TODO would be better if this is not in window scope
(window as any).registerInactivityTimerResetEvents = registerInactivityTimerResetEvents;