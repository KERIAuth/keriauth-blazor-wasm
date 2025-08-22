/// <reference types="chrome-types" />

export const addStorageChangeListener = (dotNetObject: any): void => {
    chrome.storage.onChanged.addListener(async (changes, area) => {
        if (area === 'local') {
            // Convert changes to a plain object for serialization
            const changesObj: { [key: string]: { oldValue: any, newValue: any } } = {};
            for (let [key, { oldValue, newValue }] of Object.entries(changes)) {
                changesObj[key] = { oldValue, newValue };
            }
            await dotNetObject.invokeMethodAsync('NotifyStorageChanged', changesObj, 'local');
        }
    });
};