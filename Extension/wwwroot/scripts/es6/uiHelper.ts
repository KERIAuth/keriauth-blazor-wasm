/// <reference types="chrome-types" />
// uiHelper.ts

const UIHelper = () => {

    // TODO P2 look at JSBind.Net
    // Scroll to an element on the page
    const bt_scrollToItem = (elementId: string): void => {
        const elem = document.getElementById(elementId);
        if (elem) {
            elem.scrollIntoView();
            window.location.hash = elementId;
        }
    };

    // TODO P2 use JSBind.Net
    const closeCurrentTab = (): void => {
        window.close();
    };

    // TODO P2 obsolete
    const newTabAndClosePopup = (): void => {
        createTab('/index.html?envirnoment=tab');
    };

    // TODO P1. This or WebExtesnsions and JSBind.Net equivalents need to check webpage-extension permissions. See Trello backlog
    const createTab = (urlString: string): void => {
        console.log(`UIHelper: creating extension tab: ${urlString}`);
        
        // Use window.open with a named target - this will reuse the same window
        // if it already exists, or create a new one if it doesn't
        const extensionWindow = window.open(urlString, 'KERIAuthExtension');
        
        if (extensionWindow) {
            extensionWindow.focus();
        } else {
            console.error('UIHelper: Failed to open extension window - popup might be blocked');
            // Fallback to chrome.tabs.create if available
            if (typeof chrome.tabs !== 'undefined') {
                const createProperties = { url: urlString };
                chrome.tabs.create(createProperties);
            }
        }
    };

    // TODO P2 reimplement with WebExtensions.Net or JSBind.Net
    const copy2Clipboard = async (text: string): Promise<void> =>
        navigator.clipboard.writeText(text);

    // TODO P2 reimplement with WebExtensions.Net or JSBind.Net, if this is needed at all
    const restartBlazorApp = (routeToIndex: string): void => {
        window.location.href = routeToIndex;
    };

    // TODO P2 reimplement with WebExtensions.Net
    const getChromeContexts = async (): Promise<string> => {
        if (chrome && chrome.runtime && chrome.runtime.getContexts) {
            // TODO P3 currently assumes POPUP contexts.  Should expand to other UI contexts and distinguish when there is a general action POPUP vs a popup for a specific website tabId.
            const c = await chrome.runtime.getContexts({ contextTypes: ['POPUP'] });
            console.log('chrome.runtime.getContexts: ', c);
            console.log('chrome.runtime.getContexts: ', JSON.stringify(c));
            return JSON.stringify(c);
        } else {
            console.warn('chrome.runtime.getContexts is not available.');
            return '';
        }
    };

    const closeWindow = (): void => {
        window.close();
    };

    return {
        bt_scrollToItem,
        closeCurrentTab,
        newTabAndClosePopup,
        createTab,
        copy2Clipboard,
        restartBlazorApp,
        getChromeContexts,
        closeWindow
    };
};
export const Utils = UIHelper();
