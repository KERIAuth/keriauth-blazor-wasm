const loaded = new Set();

export function loadScript(src) {
    if (loaded.has(src) || document.querySelector(`script[src="${src}"]`)) {
        loaded.add(src);
        return Promise.resolve();
    }
    return new Promise((resolve, reject) => {
        const el = document.createElement("script");
        el.src = src;
        el.onload = () => { loaded.add(src); resolve(); };
        el.onerror = () => reject(new Error(`Failed to load script: ${src}`));
        document.head.appendChild(el);
    });
}
