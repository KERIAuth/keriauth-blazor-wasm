export function registerInactivityTimerResetEvents(dotNetReference) {
    document.onmousemove = () => {
        dotNetReference.invokeMethodAsync("ResetInactivityTimer")
            .then((result) => {
            // console.log(result);
        })
            .catch((error) => {
            console.error(error);
        });
        ;
    };
    document.onkeydown = () => {
        dotNetReference.invokeMethodAsync("ResetInactivityTimer")
            .then((result) => {
            // console.log(result);
        })
            .catch((error) => {
            console.error(error);
        });
        ;
    };
}
// TODO would be better if this is not in window scope
window.registerInactivityTimerResetEvents = registerInactivityTimerResetEvents;
//# sourceMappingURL=registerInactivityEvents.js.map