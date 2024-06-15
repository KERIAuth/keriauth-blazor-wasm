export declare const Utils: {
    bt_scrollToItem: (elementId: string) => void;
    closeCurrentTab: () => void;
    newTabAndClosePopup: () => void;
    createTab: (urlString: string) => void;
    copy2Clipboard: (text: string) => Promise<void>;
    restartBlazorApp: (routeToIndex: string) => void;
};
