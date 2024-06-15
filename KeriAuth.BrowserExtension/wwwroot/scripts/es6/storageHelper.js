/// <reference types="chrome" />
export const addStorageChangeListener = (dotNetObject) => {
    chrome.storage.onChanged.addListener(async (changes, area) => {
        if (area === 'local') {
            // Convert changes to a plain object for serialization
            const changesObj = {};
            for (let [key, { oldValue, newValue }] of Object.entries(changes)) {
                changesObj[key] = { oldValue, newValue };
            }
            await dotNetObject.invokeMethodAsync('NotifyStorageChanged', changesObj, 'local');
        }
    });
};
//# sourceMappingURL=storageHelper.js.map