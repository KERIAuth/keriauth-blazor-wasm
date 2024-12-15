/// <reference types="chrome" />
// uiHelper.ts

const UIHelper = () => {

    // Scroll to an element on the page
    const bt_scrollToItem = (elementId: string): void => {
        const elem = document.getElementById(elementId);
        if (elem) {
            elem.scrollIntoView();
            window.location.hash = elementId;
        }
    }

    const closeCurrentTab = (): void => {
        window.close();
    }

    const newTabAndClosePopup = (): void => {
        createTab("/index.html?envirnoment=tab");
    }

    const createTab = (urlString: string): void => {
        console.log("UIHelper: creating extension tab: " + urlString);
        if (typeof (chrome.tabs) == 'undefined') {
            console.error('UIHelper: chrome.tabs is not available');
        } else {
            const createProperties = { url: urlString  } as chrome.tabs.CreateProperties;
            chrome.tabs.create(createProperties);
        }
    }

    const copy2Clipboard = async (text: string): Promise<void> =>
        navigator.clipboard.writeText(text);

    const restartBlazorApp = (routeToIndex: string): void => {
        window.location.href = routeToIndex;
    };

    const getChromeContexts = async (): Promise<string> => {
        if (chrome && chrome.runtime && chrome.runtime.getContexts) {
            // TODO P3 currently assumes POPUP contexts.  Should expand to other UI contexts and distinguish when there is a general action POPUP vs a popup for a specific website tabId.
            var c = await chrome.runtime.getContexts({ contextTypes: [chrome.runtime.ContextType.POPUP ]});
            console.log('chrome.runtime.getContexts: ', c);
            console.log('chrome.runtime.getContexts: ', JSON.stringify(c));
            return JSON.stringify(c);
        } else {
            console.warn('chrome.runtime.getContexts is not available.');
            return "";
        }
    }

    const closeWindow = (): void => {
        window.close();
    }

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
}
export const Utils = UIHelper();