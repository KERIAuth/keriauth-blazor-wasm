// ContentScriptMain.ts
(() => {
  if (typeof window !== "undefined" && typeof window.alert === "function") {
    window.alert("✅ ContentScriptMain is running in page context");
  } else {
    console.log("Note, this ContentScriptMain instance is not running in a page context");
  }
})();