export class StorageEvents {
    static addStorageChangeListener(dotNetObject) {
        chrome.storage.onChanged.addListener((changes, areaName) => {
            dotNetObject.invokeMethodAsync('OnStorageChanged', changes, areaName);
        });
    }
}
// TODO update signature so that callback function name (e.g. OnStorageChanged) is passed in
export function addStorageChangeListener(dotNetObject) {
    StorageEvents.addStorageChangeListener(dotNetObject);
}
//# sourceMappingURL=storageHelper.js.map